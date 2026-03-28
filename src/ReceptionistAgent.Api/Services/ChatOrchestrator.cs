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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

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
    private readonly ISessionBlacklistService _blacklistService;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private readonly ILogger<ChatOrchestrator> _logger;

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
        Microsoft.AspNetCore.SignalR.IHubContext<ReceptionistAgent.Api.Hubs.DashboardHub> hubContext,
        ISessionBlacklistService blacklistService,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ILogger<ChatOrchestrator> logger)
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
        _blacklistService = blacklistService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<OrchestrationResult> ProcessMessageAsync(
        string message,
        Guid sessionId,
        string tenantId,
        string eventTypePrefix,
        Dictionary<string, string>? additionalMetadata = null,
        string? messageId = null)
    {
        _sessionContext.SessionId = sessionId;
        var metadata = additionalMetadata ?? new Dictionary<string, string>();

        // ═══ PASO -1: Deduplicación (Evitar doble llamada por retries de Webhooks) ═══
        if (!string.IsNullOrEmpty(messageId))
        {
            var cacheKey = $"msg_proc_{messageId}";
            if (_cache.TryGetValue(cacheKey, out object? cachedObj))
            {
                var existingResult = cachedObj as OrchestrationResult;
                _logger.LogInformation("Message {MessageId} already processed/processing. Skipping.", messageId);
                return existingResult ?? new OrchestrationResult { Response = "Procesando..." };
            }

            // Marcamos como "en proceso" con un resultado vacío temporal
            _cache.Set(cacheKey, (OrchestrationResult?)null, TimeSpan.FromMinutes(10));
        }

        // ═══ PASO 0: Blacklist Check (Ahorro de recursos) ═══
        if (await _blacklistService.IsBlacklistedAsync(sessionId))
        {
            _logger.LogWarning("Blocking message from blacklisted session {SessionId}", sessionId);
            return new OrchestrationResult
            {
                Response = "Su acceso ha sido restringido temporalmente debido a actividad inusual. Por favor, intente más tarde.",
                WasFiltered = true
            };
        }

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

            if (guardResult.Level == ThreatLevel.High)
            {
                _logger.LogCritical("High threat detected for session {SessionId}. Blacklisting for 30 minutes.", sessionId);
                await _blacklistService.BlacklistSessionAsync(sessionId, TimeSpan.FromMinutes(30));
            }

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

        // --- SUMMARIZATION ---
        history = await SummarizeHistoryIfNeededAsync(history, tenantId, sessionId);

        // ═══ BROADCAST EN LÍNEA ANTES DE PENSAR ═══
        // Se añade el mensaje del usuario y se guarda para que el Dashboard lo refleje instantáneamente.
        history.AddUserMessage(message);
        await _sessionRepository.UpdateChatHistoryAsync(sessionId, tenantId, history, userPhone);
        
        if (_hubContext != null)
        {
            await _hubContext.Clients.Group(tenantId).SendAsync("ReceiveSessionUpdate");
            await _hubContext.Clients.Group(tenantId).SendAsync("NotifyTyping", sessionId, true);
        }

        string response;
        try
        {
            // Pasamos string.Empty porque ya agregamos el mensaje manualmente arriba.
            response = await _agent.RespondAsync(string.Empty, history);
        }
        catch (Exception ex) when (ex.Message.Contains("tool_use_failed"))
        {
            if (_hubContext != null) await _hubContext.Clients.Group(tenantId).SendAsync("NotifyTyping", sessionId, false);
            
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

        if (_hubContext != null) await _hubContext.Clients.Group(tenantId).SendAsync("NotifyTyping", sessionId, false);

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

        var orchestrationResult = new OrchestrationResult
        {
            Response = filterResult.FilteredContent,
            WasFiltered = filterResult.WasModified,
            RedactedItems = filterResult.RedactedItems
        };

        // Cachear resultado final para deduplicación
        if (!string.IsNullOrEmpty(messageId))
        {
            _cache.Set($"msg_proc_{messageId}", orchestrationResult, TimeSpan.FromMinutes(10));
        }

        return orchestrationResult;
    }

    private async Task<Microsoft.SemanticKernel.ChatCompletion.ChatHistory> SummarizeHistoryIfNeededAsync(
        Microsoft.SemanticKernel.ChatCompletion.ChatHistory history, 
        string tenantId, 
        Guid sessionId)
    {
        // Contamos solo mensajes de usuario y asistente (excluimos el system prompt inicial)
        var talkMessages = history.Where(m => m.Role != Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System).ToList();
        
        if (talkMessages.Count <= 12) return history;

        _logger.LogInformation("Summarizing history for session {SessionId} (Total: {Count})", sessionId, talkMessages.Count);

        // Tomamos los primeros 10 mensajes para resumir
        var toSummarize = talkMessages.Take(10).ToList();
        var remaining = talkMessages.Skip(10).ToList();

        var summaryPrompt = "Resume los puntos clave de esta conversación previa entre un cliente y una secretaria virtual de forma muy concisa. " +
                           "Enfócate en: Nombre del cliente, servicios de interés, fechas mencionadas y estado de la cita. " +
                           "Conversación:\n" + string.Join("\n", toSummarize.Select(m => $"{m.Role}: {m.Content}"));

        try
        {
            var summary = await _agent.RespondAsync(summaryPrompt, new Microsoft.SemanticKernel.ChatCompletion.ChatHistory());
            
            // Reconstruir historia: System Prompt + Resumen + Restantes
            var newHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(history.First(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System).Content!);
            newHistory.AddSystemMessage($"[RESUMEN DE CONVERSACIÓN PREVIA]: {summary}");
            
            foreach (var msg in remaining)
            {
                newHistory.AddMessage(msg.Role, msg.Content!);
            }

            return newHistory;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to summarize history for session {SessionId}", sessionId);
            return history;
        }
    }
}
