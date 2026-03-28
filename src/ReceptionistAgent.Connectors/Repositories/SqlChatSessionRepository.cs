using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Connectors.Repositories;

public class SqlChatSessionRepository : IChatSessionRepository
{
    private readonly string _agentCoreConnectionString;
    private readonly string? _tenantConnectionString;

    public SqlChatSessionRepository(string agentCoreConnectionString, string? tenantConnectionString = null, ILoggerFactory? loggerFactory = null)
    {
        _agentCoreConnectionString = agentCoreConnectionString;
        _tenantConnectionString = tenantConnectionString;
    }

    public async Task<ChatHistory> GetChatHistoryAsync(Guid sessionId, string tenantId, string systemPrompt, string? userPhone = null)
    {
        // Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string sql = "SELECT HistoryJson FROM ChatSessions WHERE Id = @Id";
                using var connection = new SqlConnection(_tenantConnectionString);
                var json = await connection.QuerySingleOrDefaultAsync<string>(sql, new { Id = sessionId });

                if (!string.IsNullOrWhiteSpace(json))
                {
                    return MapToHistory(json, systemPrompt);
                }
            }
            catch
            {
                // Fallback to AgentCore if tenant DB fails or record not found
            }
        }

        // Fallback/Legacy: AgentCore
        const string coreSql = "SELECT HistoryJson FROM ChatSessions WHERE Id = @Id AND TenantId = @TenantId";
        using (var connection = new SqlConnection(_agentCoreConnectionString))
        {
            var json = await connection.QuerySingleOrDefaultAsync<string>(coreSql, new { Id = sessionId, TenantId = tenantId });

            if (!string.IsNullOrWhiteSpace(json))
            {
                return MapToHistory(json, systemPrompt);
            }
        }

        // New history
        var newHistory = string.IsNullOrWhiteSpace(systemPrompt) ? new ChatHistory() : new ChatHistory(systemPrompt);
        await InsertChatHistoryAsync(sessionId, tenantId, newHistory, userPhone);
        return newHistory;
    }

    private static ChatHistory MapToHistory(string json, string systemPrompt)
    {
        try
        {
            var history = new ChatHistory();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string roleStr = "user";
                    if (element.TryGetProperty("Role", out var roleProp) || element.TryGetProperty("role", out roleProp))
                    {
                        if (roleProp.ValueKind == JsonValueKind.Object && roleProp.TryGetProperty("Label", out var labelProp))
                        {
                            roleStr = labelProp.GetString() ?? "user";
                        }
                        else if (roleProp.ValueKind == JsonValueKind.String)
                        {
                            roleStr = roleProp.GetString() ?? "user";
                        }
                    }

                    string content = string.Empty;
                    if (element.TryGetProperty("Content", out var contentProp) || element.TryGetProperty("content", out contentProp))
                    {
                        content = contentProp.GetString() ?? string.Empty;
                    }

                    DateTime timestamp = DateTime.UtcNow;
                    if (element.TryGetProperty("Timestamp", out var tsProp) || element.TryGetProperty("timestamp", out tsProp))
                    {
                        if (tsProp.TryGetDateTime(out var dt)) timestamp = dt;
                    }

                    if (!roleStr.Equals("system", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        var msg = new ChatMessageContent(new AuthorRole(roleStr.ToLower()), content);
                        msg.Metadata = new Dictionary<string, object?> { ["Timestamp"] = timestamp };
                        history.Add(msg);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                history.Insert(0, new ChatMessageContent(AuthorRole.System, systemPrompt));
            }
            return history;
        }
        catch
        {
            return string.IsNullOrWhiteSpace(systemPrompt) ? new ChatHistory() : new ChatHistory(systemPrompt);
        }
    }

    public async Task UpdateChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone = null)
    {
        var persistableMessages = history
            .Select(m => new { 
                Role = m.Role.Label, 
                Content = m.Content,
                Timestamp = m.Metadata != null && m.Metadata.ContainsKey("Timestamp") ? m.Metadata["Timestamp"] : DateTime.UtcNow
            })
            .ToList();
        var json = JsonSerializer.Serialize(persistableMessages);

        // 1. Primary Write (Tenant DB)
        bool updatedInTenant = false;
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            const string tenantSql = "UPDATE ChatSessions SET HistoryJson = @HistoryJson, UpdatedAt = @UpdatedAt WHERE Id = @Id";
            using var connection = new SqlConnection(_tenantConnectionString);
            var rows = await connection.ExecuteAsync(tenantSql, new
            {
                HistoryJson = json,
                UpdatedAt = DateTime.UtcNow,
                Id = sessionId
            });
            updatedInTenant = rows > 0;
        }

        // 2. Backup Write (AgentCore) - Fire & Forget
        _ = Task.Run(async () =>
        {
            try
            {
                const string coreSql = "UPDATE ChatSessions SET HistoryJson = @HistoryJson, UpdatedAt = @UpdatedAt WHERE Id = @Id AND TenantId = @TenantId";
                using var connection = new SqlConnection(_agentCoreConnectionString);
                var rows = await connection.ExecuteAsync(coreSql, new
                {
                    HistoryJson = json,
                    UpdatedAt = DateTime.UtcNow,
                    Id = sessionId,
                    TenantId = tenantId
                });

                if (rows == 0 && !updatedInTenant)
                {
                    // If it wasn't in AgentCore and we couldn't update it anywhere, insert it (sync might be better here but following dual pattern)
                    await InsertChatHistoryAsync(sessionId, tenantId, history, userPhone);
                }
            }
            catch
            {
                // Log warning only
            }
        });

        if (!updatedInTenant && !string.IsNullOrEmpty(_tenantConnectionString))
        {
            // If it's a new session for the tenant DB, we insert it synchronously
            await InsertChatHistoryAsync(sessionId, tenantId, history, userPhone);
        }
    }

    private async Task InsertChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone)
    {
        var persistableMessages = history
            .Select(m => new { 
                Role = m.Role.Label, 
                Content = m.Content,
                Timestamp = m.Metadata != null && m.Metadata.ContainsKey("Timestamp") ? m.Metadata["Timestamp"] : DateTime.UtcNow
            })
            .ToList();
        var json = JsonSerializer.Serialize(persistableMessages);
        var now = DateTime.UtcNow;

        // 1. Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string tenantSql = @"
                    IF NOT EXISTS (SELECT 1 FROM ChatSessions WHERE Id = @Id)
                    BEGIN
                        INSERT INTO ChatSessions (Id, UserPhone, HistoryJson, NeedsHumanAttention, CreatedAt, UpdatedAt)
                        VALUES (@Id, @UserPhone, @HistoryJson, 0, @CreatedAt, @UpdatedAt)
                    END
                    ELSE
                    BEGIN
                        UPDATE ChatSessions SET HistoryJson = @HistoryJson, UpdatedAt = @UpdatedAt 
                        WHERE Id = @Id
                    END";

                using var connection = new SqlConnection(_tenantConnectionString);
                await connection.ExecuteAsync(tenantSql, new
                {
                    Id = sessionId,
                    UserPhone = userPhone ?? string.Empty,
                    HistoryJson = json,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            catch { /* Continue to backup */ }
        }

        // 2. Backup: AgentCore
        try
        {
            const string coreSql = @"
                IF NOT EXISTS (SELECT 1 FROM ChatSessions WHERE Id = @Id)
                BEGIN
                    INSERT INTO ChatSessions (Id, TenantId, UserPhone, HistoryJson, NeedsHumanAttention, CreatedAt, UpdatedAt)
                    VALUES (@Id, @TenantId, @UserPhone, @HistoryJson, 0, @CreatedAt, @UpdatedAt)
                END
                ELSE
                BEGIN
                    UPDATE ChatSessions SET HistoryJson = @HistoryJson, UpdatedAt = @UpdatedAt 
                    WHERE Id = @Id AND TenantId = @TenantId
                END";

            using var coreConnection = new SqlConnection(_agentCoreConnectionString);
            await coreConnection.ExecuteAsync(coreSql, new
            {
                Id = sessionId,
                TenantId = tenantId,
                UserPhone = userPhone ?? string.Empty,
                HistoryJson = json,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        catch { /* Robustness for backup write */ }
    }

    public async Task SetNeedsHumanAttentionAsync(Guid sessionId, string tenantId, bool needsAttention)
    {
        // 1. Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            const string tenantSql = "UPDATE ChatSessions SET NeedsHumanAttention = @NeedsAttention, UpdatedAt = @UpdatedAt WHERE Id = @Id";
            using var connection = new SqlConnection(_tenantConnectionString);
            await connection.ExecuteAsync(tenantSql, new
            {
                NeedsAttention = needsAttention ? 1 : 0,
                UpdatedAt = DateTime.UtcNow,
                Id = sessionId
            });
        }

        // 2. Backup: AgentCore - Fire & Forget
        _ = Task.Run(async () =>
        {
            try
            {
                const string coreSql = "UPDATE ChatSessions SET NeedsHumanAttention = @NeedsAttention, UpdatedAt = @UpdatedAt WHERE Id = @Id AND TenantId = @TenantId";
                using var connection = new SqlConnection(_agentCoreConnectionString);
                await connection.ExecuteAsync(coreSql, new
                {
                    NeedsAttention = needsAttention ? 1 : 0,
                    UpdatedAt = DateTime.UtcNow,
                    Id = sessionId,
                    TenantId = tenantId
                });
            }
            catch { }
        });
    }

    public async Task<List<ReceptionistAgent.Core.Models.ChatSessionDto>> GetActiveSessionsAsync(string tenantId)
    {
        // Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string sql = @"
                    SELECT Id, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt 
                    FROM ChatSessions 
                    ORDER BY UpdatedAt DESC";

                using var connection = new SqlConnection(_tenantConnectionString);
                var sessions = await connection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(sql);
                return sessions.Select(s => { s.TenantId = tenantId; return s; }).ToList();
            }
            catch { }
        }

        // Fallback: AgentCore
        const string coreSql = @"
            SELECT Id, TenantId, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt 
            FROM ChatSessions 
            WHERE TenantId = @TenantId 
            ORDER BY UpdatedAt DESC";

        using var coreConnection = new SqlConnection(_agentCoreConnectionString);
        var coreSessions = await coreConnection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(coreSql, new { TenantId = tenantId });
        return coreSessions.ToList();
    }

    public async Task<List<ReceptionistAgent.Core.Models.ChatSessionDto>> GetSessionsByPhoneAsync(string tenantId, string phone)
    {
        // Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string sql = @"
                    SELECT Id, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt 
                    FROM ChatSessions 
                    WHERE UserPhone = @Phone
                    ORDER BY UpdatedAt DESC";

                using var connection = new SqlConnection(_tenantConnectionString);
                var sessions = await connection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(sql, new { Phone = phone });
                return sessions.Select(s => { s.TenantId = tenantId; return s; }).ToList();
            }
            catch { }
        }

        // Fallback: AgentCore
        const string coreSql = @"
            SELECT Id, TenantId, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt 
            FROM ChatSessions 
            WHERE TenantId = @TenantId AND UserPhone = @Phone
            ORDER BY UpdatedAt DESC";

        using var coreConnection = new SqlConnection(_agentCoreConnectionString);
        var coreSessions = await coreConnection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(coreSql, new { TenantId = tenantId, Phone = phone });
        return coreSessions.ToList();
    }
}
