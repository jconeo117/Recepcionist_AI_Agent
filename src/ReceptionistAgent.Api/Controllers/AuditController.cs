using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// Endpoints de auditoría para consultar registro de interacciones y eventos de seguridad.
/// Acceso global (no requiere X-Tenant-Id), permite auditar todos los tenants.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class AuditController : ControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public AuditController(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Obtiene el historial de auditoría de una sesión específica.
    /// </summary>
    [HttpGet("session/{sessionId:guid}")]
    public async Task<IActionResult> GetSessionAudit(Guid sessionId)
    {
        var entries = await _auditLogger.GetSessionAuditAsync(sessionId);

        if (!entries.Any())
            return NotFound(new { message = "No se encontraron registros para esta sesión." });

        return Ok(new
        {
            sessionId,
            totalEntries = entries.Count,
            entries = entries.Select(e => new
            {
                e.Id,
                e.TenantId,
                e.Timestamp,
                e.EventType,
                e.Content,
                threatLevel = e.ThreatLevel?.ToString(),
                e.Metadata
            })
        });
    }

    /// <summary>
    /// Obtiene eventos de seguridad en un rango de fechas.
    /// </summary>
    [HttpGet("security")]
    public async Task<IActionResult> GetSecurityEvents(
        [FromQuery] string? tenantId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-7);
        var toDate = to ?? DateTime.UtcNow;

        var entries = await _auditLogger.GetSecurityEventsAsync(tenantId, fromDate, toDate);

        return Ok(new
        {
            tenantId = tenantId ?? "all",
            period = new { from = fromDate, to = toDate },
            totalEvents = entries.Count,
            events = entries.Select(e => new
            {
                e.Id,
                e.TenantId,
                e.SessionId,
                e.Timestamp,
                e.EventType,
                e.Content,
                threatLevel = e.ThreatLevel?.ToString(),
                e.Metadata
            })
        });
    }

    /// <summary>
    /// Lista los últimos eventos de auditoría. Filtro opcional por tenant.
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentEvents(
        [FromQuery] string? tenantId = null,
        [FromQuery] int limit = 50)
    {
        var entries = await _auditLogger.GetAllEventsAsync(tenantId, Math.Min(limit, 200));

        return Ok(new
        {
            tenantId = tenantId ?? "all",
            totalEntries = entries.Count,
            entries = entries.Select(e => new
            {
                e.Id,
                e.TenantId,
                e.SessionId,
                e.Timestamp,
                e.EventType,
                e.Content,
                threatLevel = e.ThreatLevel?.ToString(),
                e.Metadata
            })
        });
    }
}
