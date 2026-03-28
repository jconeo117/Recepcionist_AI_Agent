using Dapper;
using Npgsql;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Connectors.Security;
using System.Text.Json;

namespace ReceptionistAgent.Connectors.Security;

public class PostgreSqlAuditLogger : IAuditLogger
{
    private readonly string _connectionString;

    public PostgreSqlAuditLogger(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task LogAsync(AuditEntry entry)
    {
        entry.Timestamp = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO audits (
                id, tenant_id, session_id, timestamp, event_type, content, threat_level, metadata
            ) VALUES (
                @Id, @TenantId, @SessionId, @Timestamp, @EventType, @Content, @ThreatLevel, CAST(@Metadata AS jsonb)
            )";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            entry.Id,
            entry.TenantId,
            entry.SessionId,
            entry.Timestamp,
            entry.EventType,
            entry.Content,
            ThreatLevel = entry.ThreatLevel?.ToString(),
            Metadata = JsonSerializer.Serialize(entry.Metadata)
        });
    }

    public async Task<List<AuditEntry>> GetSessionAuditAsync(Guid sessionId)
    {
        const string sql = "SELECT id AS \"Id\", tenant_id AS \"TenantId\", session_id AS \"SessionId\", timestamp AS \"Timestamp\", event_type AS \"EventType\", content AS \"Content\", threat_level AS \"ThreatLevel\", metadata AS \"Metadata\" FROM audits WHERE session_id = @SessionId ORDER BY timestamp ASC";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<AuditEntity>(sql, new { SessionId = sessionId });

        return entities.Select(MapToEntry).ToList();
    }

    public async Task<List<AuditEntry>> GetSecurityEventsAsync(string? tenantId, DateTime from, DateTime to)
    {
        var sql = @"
            SELECT id AS ""Id"", tenant_id AS ""TenantId"", session_id AS ""SessionId"", timestamp AS ""Timestamp"", event_type AS ""EventType"", content AS ""Content"", threat_level AS ""ThreatLevel"", metadata AS ""Metadata""
            FROM audits 
            WHERE event_type IN ('SecurityBlock', 'OutputFiltered') 
              AND timestamp >= @From AND timestamp <= @To";

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            sql += " AND tenant_id = @TenantId";
        }

        sql += " ORDER BY timestamp DESC";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<AuditEntity>(sql, new
        {
            From = from,
            To = to,
            TenantId = tenantId
        });

        return entities.Select(MapToEntry).ToList();
    }

    public async Task<List<AuditEntry>> GetAllEventsAsync(string? tenantId, int limit = 100)
    {
        var sql = "SELECT id AS \"Id\", tenant_id AS \"TenantId\", session_id AS \"SessionId\", timestamp AS \"Timestamp\", event_type AS \"EventType\", content AS \"Content\", threat_level AS \"ThreatLevel\", metadata AS \"Metadata\" FROM audits";

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            sql += " WHERE tenant_id = @TenantId";
        }

        sql += " ORDER BY timestamp DESC";

        if (limit > 0)
        {
            sql += $" LIMIT {limit}";
        }

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<AuditEntity>(sql, new { TenantId = tenantId });

        return entities.Select(MapToEntry).ToList();
    }

    private static AuditEntry MapToEntry(AuditEntity entity)
    {
        return new AuditEntry
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            SessionId = entity.SessionId,
            Timestamp = entity.Timestamp,
            EventType = entity.EventType,
            Content = entity.Content,
            ThreatLevel = Enum.TryParse<Core.Security.ThreatLevel>(entity.ThreatLevel, true, out var level) ? level : null,
            Metadata = string.IsNullOrWhiteSpace(entity.Metadata)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Metadata) ?? new Dictionary<string, string>()
        };
    }
}
