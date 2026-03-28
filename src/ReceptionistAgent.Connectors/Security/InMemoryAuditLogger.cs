using System.Collections.Concurrent;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Security;

namespace ReceptionistAgent.Connectors.Security;

/// <summary>
/// Implementación in-memory del audit logger.
/// Thread-safe para uso en escenarios concurrentes.
/// Preparada para reemplazar con implementación de base de datos.
/// </summary>
public class InMemoryAuditLogger : IAuditLogger
{
    private readonly ConcurrentBag<AuditEntry> _entries = [];

    public Task LogAsync(AuditEntry entry)
    {
        entry.Timestamp = DateTime.UtcNow;
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<List<AuditEntry>> GetSessionAuditAsync(Guid sessionId)
    {
        var results = _entries
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return Task.FromResult(results);
    }

    public Task<List<AuditEntry>> GetSecurityEventsAsync(string? tenantId, DateTime from, DateTime to)
    {
        var query = _entries
            .Where(e => e.EventType is "SecurityBlock" or "OutputFiltered")
            .Where(e => e.Timestamp >= from && e.Timestamp <= to);

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(e => e.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(query.OrderByDescending(e => e.Timestamp).ToList());
    }

    public Task<List<AuditEntry>> GetAllEventsAsync(string? tenantId, int limit = 100)
    {
        var query = _entries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(e => e.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(query.OrderByDescending(e => e.Timestamp).Take(limit).ToList());
    }
}
