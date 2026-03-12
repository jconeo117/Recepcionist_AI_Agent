using ReceptionistAgent.AI.Agents;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Security;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReceptionistAgent.Api.Services;

public class ChatOrchestrator : IChatOrchestrator
{
    private readonly IRecepcionistAgent _agent;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IClientDataAdapter _adapter;
    private readonly IInputGuard _inputGuard;
    private readonly IOutputFilter _outputFilter;
    private readonly IAuditLogger _auditLogger;
    private readonly TenantContext _tenantContext;
    private readonly ISessionContext _sessionContext;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<ReceptionistAgent.Api.Hubs.DashboardHub> _hubContext;

    public ChatOrchestrator(
        IRecepcionistAgent agent,
        IChatSessionRepository sessionRepository,
        IPromptBuilder promptBuilder,
        IClientDataAdapter adapter,
        IInputGuard inputGuard,
        IOutputFilter outputFilter,
        IAuditLogger auditLogger,
        TenantContext tenantContext,
        ISessionContext sessionContext,
        Microsoft.AspNetCore.SignalR.IHubContext<ReceptionistAgent.Api.Hubs.DashboardHub> hubContext)
    {
        _agent = agent;
        _sessionRepository = sessionRepository;
        _promptBuilder = promptBuilder;
        _adapter = adapter;
        _inputGuard = inputGuard;
        _outputFilter = outputFilter;
        _auditLogger = auditLogger;
        _tenantContext = tenantContext;
        _sessionContext = sessionContext;
        _hubContext = hubContext;
    }

    public async Task<OrchestrationResult> ProcessMessageAsync(
        string message,
        Guid sessionId,
        string tenantId,
        string eventTypePrefix,
        Dictionary<string, string>? additionalMetadata = null)
    {
        _sessionContext.SessionId = sessionId;
        var metadata = additionalMetadata ?? new Dictionary<string, string>();

        // ═══ PASO 1: Input Guard ═══
        var guardResult = await _inputGuard.AnalyzeAsync(message);

        await _auditLogger.LogAsync(new AuditEntry
        {
            TenantId = tenantId,
            SessionId = sessionId,
            EventType = $"{eventTypePrefix}UserMessage",
            Content = message,
            ThreatLevel = guardResult.Level,
            Metadata = metadata
        });

        if (!guardResult.IsAllowed)
        {
            metadata["originalMessage"] = message;

            await _auditLogger.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                SessionId = sessionId,
                EventType = "SecurityBlock",
                Content = guardResult.RejectionReason ?? "Mensaje bloqueado",
                ThreatLevel = guardResult.Level,
                Metadata = metadata
            });

            return new OrchestrationResult
            {
                Response = guardResult.RejectionReason ?? "Solo puedo ayudarle con la gestión de citas. ¿Desea agendar una cita?",
                WasFiltered = true
            };
        }

        // ═══ PASO 2: Procesar con el Agente ═══
        var providers = await _adapter.GetAllProvidersAsync();

        if (_tenantContext.CurrentTenant == null)
        {
            return new OrchestrationResult
            {
                Response = "Error interno: no se pudo resolver el tenant para esta solicitud.",
                WasFiltered = true
            };
        }

        var userPhone = metadata.TryGetValue("phone", out var p) ? p : null;
        var systemPrompt = await _promptBuilder.BuildSystemPromptAsync(_tenantContext.CurrentTenant, providers);
        var history = await _sessionRepository.GetChatHistoryAsync(sessionId, tenantId, systemPrompt, userPhone);

        string response;
        try
        {
            response = await _agent.RespondAsync(message, history);
        }
        catch (Exception ex) when (ex.Message.Contains("tool_use_failed"))
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var historyJson = System.Text.Json.JsonSerializer.Serialize(history, options);
            Console.WriteLine($"\n[GROQ TOOL_USE_FAILED DEBUG] --- ChatHistory sent to Groq:\n{historyJson}\n------------------\n");

            // Retornar mensaje amigable en lugar de crash con 500
            return new OrchestrationResult
            {
                Response = "Disculpe, tuve un problema procesando su solicitud. ¿Podría reformular su mensaje o intentar de nuevo?",
                WasFiltered = false
            };
        }

        await _sessionRepository.UpdateChatHistoryAsync(sessionId, tenantId, history, userPhone);

        // ═══ PASO 3: Output Filter ═══
        var allowedPhones = _tenantContext.CurrentTenant?.Phone != null
            ? new[] { _tenantContext.CurrentTenant.Phone }
            : null;
        var filterResult = await _outputFilter.FilterAsync(response, tenantId, allowedPhones, new[] { message });

        if (filterResult.WasModified)
        {
            var filterMetadata = new Dictionary<string, string>(metadata)
            {
                ["redactedItems"] = string.Join(", ", filterResult.RedactedItems),
                ["originalLength"] = response.Length.ToString()
            };

            await _auditLogger.LogAsync(new AuditEntry
            {
                TenantId = tenantId,
                SessionId = sessionId,
                EventType = "OutputFiltered",
                Content = "Respuesta filtrada por seguridad",
                Metadata = filterMetadata
            });
        }

        await _auditLogger.LogAsync(new AuditEntry
        {
            TenantId = tenantId,
            SessionId = sessionId,
            EventType = $"{eventTypePrefix}AgentResponse",
            Content = filterResult.FilteredContent,
            Metadata = metadata
        });

        // Broadcast real-time update to the Client Dashboard via SignalR WebSockets
        if (_hubContext != null)
        {
            await _hubContext.Clients.Group(tenantId).SendAsync("ReceiveSessionUpdate");
        }

        return new OrchestrationResult
        {
            Response = filterResult.FilteredContent,
            WasFiltered = filterResult.WasModified,
            RedactedItems = filterResult.RedactedItems
        };
    }
}
