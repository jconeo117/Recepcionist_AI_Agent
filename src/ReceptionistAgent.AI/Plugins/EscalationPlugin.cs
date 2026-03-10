using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.AI.Services;

namespace ReceptionistAgent.AI.Plugins;

/// <summary>
/// Plugin que permite al agente escalar la conversación a un humano
/// cuando se encuentra en una situación fuera de su alcance o el usuario lo pide.
/// </summary>
public class EscalationPlugin
{
    private readonly ILogger<EscalationPlugin> _logger;
    private readonly IEscalationService _escalationService;
    private readonly string _tenantId;
    private readonly Guid _sessionId;

    public EscalationPlugin(
        ILogger<EscalationPlugin> logger,
        IEscalationService escalationService,
        string tenantId,
        Guid sessionId)
    {
        _logger = logger;
        _escalationService = escalationService;
        _tenantId = tenantId;
        _sessionId = sessionId;
    }

    [KernelFunction("escalate_to_human")]
    [Description("Transfiere la conversación al equipo de agentes humanos. Úsalo SIEMPRE que el usuario lo solicite explícitamente, exprese frustración extrema, o pregunte por temas urgentes complejos que no puedes resolver automáticamente.")]
    public async Task<string> EscalateToHumanAsync(
        [Description("Razón breve y concisa de por qué se transfiere a humano")] string reason)
    {
        _logger.LogInformation("Escalating session {SessionId} to human. Reason: {Reason}", _sessionId, reason);

        await _escalationService.EscalateSessionAsync(_sessionId, _tenantId, reason);

        return "La sesión ha sido escalada a un agente humano. Comunícale al usuario que un representante lo atenderá a la brevedad posible.";
    }
}
