using Dapper;
using Npgsql;
using Microsoft.Data.SqlClient;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReceptionistAgent.Connectors.Repositories;

public class PostgreSqlChatSessionRepository : IChatSessionRepository
{
    private readonly string _agentCoreConnectionString;
    private readonly string? _tenantConnectionString;

    private readonly ILogger<PostgreSqlChatSessionRepository> _logger;
    private readonly bool _isCorePostgres;

    public PostgreSqlChatSessionRepository(string agentCoreConnectionString, string? tenantConnectionString = null, ILoggerFactory? loggerFactory = null)
    {
        _agentCoreConnectionString = agentCoreConnectionString;
        _tenantConnectionString = tenantConnectionString;
        _isCorePostgres = agentCoreConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PostgreSqlChatSessionRepository>();
    }

    public async Task<ChatHistory> GetChatHistoryAsync(Guid sessionId, string tenantId, string systemPrompt, string? userPhone = null)
    {
        // Primary: Tenant DB (PostgreSQL) - Read from chat_messages table
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                using var connection = new NpgsqlConnection(_tenantConnectionString);
                
                // Simplified migration: ensure table exists and has unique constraint
                const string createTableSql = @"
                    CREATE TABLE IF NOT EXISTS chat_messages (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
                        role VARCHAR(50) NOT NULL,
                        content TEXT NOT NULL,
                        timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
                        order_index INT NOT NULL DEFAULT 0,
                        metadata JSONB
                    );
                    CREATE INDEX IF NOT EXISTS idx_chat_messages_session ON chat_messages(session_id);
                    
                    -- Migration: Add order_index if missing
                    DO $$ 
                    BEGIN 
                        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'chat_messages' AND column_name = 'order_index') THEN
                            ALTER TABLE chat_messages ADD COLUMN order_index INT NOT NULL DEFAULT 0;
                        END IF;
                    END $$;

                    -- Robust deduplication constraint
                    DO $$ 
                    BEGIN 
                        IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'idx_chat_messages_unique') THEN
                            ALTER TABLE chat_messages DROP CONSTRAINT idx_chat_messages_unique;
                        END IF;
                        
                        -- Cleanup existing duplicates if any (based on order_index)
                        -- We keep the one with the smallest ID
                        DELETE FROM chat_messages a USING chat_messages b 
                        WHERE a.id > b.id AND a.session_id = b.session_id AND a.order_index = b.order_index;

                        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'idx_chat_messages_order_unique') THEN
                            ALTER TABLE chat_messages ADD CONSTRAINT idx_chat_messages_order_unique UNIQUE (session_id, order_index);
                        END IF;
                    END $$;";
                await connection.ExecuteAsync(createTableSql);

                const string sql = "SELECT role as Role, content as Content, timestamp as Timestamp FROM chat_messages WHERE session_id = @Id ORDER BY order_index ASC, timestamp ASC";
                var messages = await connection.QueryAsync<dynamic>(sql, new { Id = sessionId });

                if (messages.Any())
                {
                    var history = new ChatHistory();
                    if (!string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        history.Add(new ChatMessageContent(AuthorRole.System, systemPrompt));
                    }

                    foreach (var m in messages)
                    {
                        var role = new AuthorRole(m.role.ToLower());
                        var msg = new ChatMessageContent(role, m.content);
                        msg.Metadata = new Dictionary<string, object?> { ["Timestamp"] = m.timestamp };
                        history.Add(msg);
                    }
                    return history;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading from chat_messages for session {Id}. Falling back to history_json.", sessionId);
            }
        }

        // Fallback: AgentCore (history_json blob)
        string coreSql = _isCorePostgres
            ? "SELECT history_json FROM chat_sessions WHERE id = @Id"
            : "SELECT HistoryJson FROM ChatSessions WHERE Id = @Id AND TenantId = @TenantId";

        using (var connection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString))
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

    private static ChatHistory MapToHistory(string json, string currentSystemPrompt)
    {
        try
        {
            var history = new ChatHistory();
            using var doc = JsonDocument.Parse(json);
            
            // New Format: Object with ResolvedPrompt snapshot
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("ResolvedPrompt", out var promptProp))
            {
                string storedPrompt = promptProp.GetString() ?? currentSystemPrompt;
                if (!string.IsNullOrWhiteSpace(storedPrompt))
                {
                    history.Add(new ChatMessageContent(AuthorRole.System, storedPrompt));
                }
                return history;
            }

            // Legacy Format: Array of messages
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

                    DateTime timestamp = DateTime.UtcNow;
                    if (element.TryGetProperty("Timestamp", out var tsProp) || element.TryGetProperty("timestamp", out tsProp))
                    {
                        if (tsProp.TryGetDateTime(out var dt)) timestamp = dt;
                    }

                    string content = string.Empty;
                    if (element.TryGetProperty("Content", out var contentProp) || element.TryGetProperty("content", out contentProp))
                    {
                        content = contentProp.GetString() ?? string.Empty;
                    }

                    if (!roleStr.Equals("system", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = new ChatMessageContent(new AuthorRole(roleStr.ToLower()), content);
                        msg.Metadata = new Dictionary<string, object?> { ["Timestamp"] = timestamp };
                        history.Add(msg);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentSystemPrompt))
            {
                history.Insert(0, new ChatMessageContent(AuthorRole.System, currentSystemPrompt));
            }
            return history;
        }
        catch
        {
            return string.IsNullOrWhiteSpace(currentSystemPrompt) ? new ChatHistory() : new ChatHistory(currentSystemPrompt);
        }
    }

    public async Task UpdateChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone = null)
    {
        // Extract resolved system prompt for diagnostic snapshot
        string resolvedPrompt = history.FirstOrDefault(m => m.Role == AuthorRole.System)?.Content ?? string.Empty;
        var promptSnapshotJson = JsonSerializer.Serialize(new { 
            ResolvedPrompt = resolvedPrompt,
            UpdatedAt = DateTime.UtcNow
        });

        // 1. Primary Write (Tenant DB - PostgreSQL)
        bool updatedInTenant = false;
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                using var connection = new NpgsqlConnection(_tenantConnectionString);
                
                // --- Part A: Update history_json column with Prompt Snapshot (Diagnostic) ---
                const string updateJsonSql = "UPDATE chat_sessions SET history_json = @Snapshot::jsonb, updated_at = @UpdatedAt WHERE id = @Id";
                await connection.ExecuteAsync(updateJsonSql, new { Snapshot = promptSnapshotJson, UpdatedAt = DateTime.UtcNow, Id = sessionId });

                // --- Part B: Sync chat_messages table (Relational Source of Truth) ---
                var messagesInHistory = history.ToList();
                for (int i = 0; i < messagesInHistory.Count; i++)
                {
                    var msg = messagesInHistory[i];
                    
                    // Skip the initial system prompt to avoid filling the table with the same prompt over and over
                    if (msg.Role == AuthorRole.System && i == 0) continue;

                    const string insertMsgSql = @"
                        INSERT INTO chat_messages (session_id, role, content, timestamp, order_index) 
                        VALUES (@SessionId, @Role, @Content, @Timestamp, @OrderIndex)
                        ON CONFLICT (session_id, order_index) DO NOTHING";
                    
                    await connection.ExecuteAsync(insertMsgSql, new {
                        SessionId = sessionId,
                        Role = msg.Role.Label,
                        Content = msg.Content,
                        Timestamp = msg.Metadata != null && msg.Metadata.ContainsKey("Timestamp") ? (DateTime)msg.Metadata["Timestamp"]! : DateTime.UtcNow,
                        OrderIndex = i
                    });
                }

                updatedInTenant = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating chat_messages for session {Id}", sessionId);
            }
        }

        // 2. Backup Write (AgentCore) - Fire & Forget
        _ = Task.Run(async () =>
        {
            try
            {
                string coreSql = _isCorePostgres
                    ? "UPDATE chat_sessions SET history_json = @Snapshot::jsonb, updated_at = @UpdatedAt WHERE id = @Id"
                    : "UPDATE ChatSessions SET HistoryJson = @Snapshot, UpdatedAt = @UpdatedAt WHERE Id = @Id AND TenantId = @TenantId";

                using var connection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString);
                await connection.ExecuteAsync(coreSql, new
                {
                    Snapshot = promptSnapshotJson,
                    UpdatedAt = DateTime.UtcNow,
                    Id = sessionId,
                    TenantId = tenantId
                });
            }
            catch { }
        });
    }

    private async Task InsertChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone)
    {
        string resolvedPrompt = history.FirstOrDefault(m => m.Role == AuthorRole.System)?.Content ?? string.Empty;
        var promptSnapshotJson = JsonSerializer.Serialize(new { 
            ResolvedPrompt = resolvedPrompt,
            UpdatedAt = DateTime.UtcNow
        });
        
        var now = DateTime.UtcNow;

        // 1. Primary: Tenant DB (PostgreSQL)
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string tenantSql = @"
                    INSERT INTO chat_sessions (id, user_phone, history_json, needs_human_attention, created_at, updated_at)
                    VALUES (@Id, @UserPhone, @Snapshot::jsonb, false, @CreatedAt, @UpdatedAt)
                    ON CONFLICT (id) DO UPDATE SET 
                        history_json = EXCLUDED.history_json, 
                        updated_at = EXCLUDED.updated_at";

                using var connection = new NpgsqlConnection(_tenantConnectionString);
                await connection.ExecuteAsync(tenantSql, new
                {
                    Id = sessionId,
                    UserPhone = userPhone ?? string.Empty,
                    Snapshot = promptSnapshotJson,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                
                // Also initialize chat_messages if they exist in history
                var messages = history.ToList();
                for (int i = 0; i < messages.Count; i++)
                {
                    var msg = messages[i];
                    if (msg.Role == AuthorRole.System && i == 0) continue; // Skip initial system prompt

                    const string insertMsgSql = "INSERT INTO chat_messages (session_id, role, content, timestamp, order_index) VALUES (@SessionId, @Role, @Content, @Timestamp, @OrderIndex) ON CONFLICT (session_id, order_index) DO NOTHING";
                    await connection.ExecuteAsync(insertMsgSql, new {
                        SessionId = sessionId,
                        Role = msg.Role.Label,
                        Content = msg.Content,
                        Timestamp = msg.Metadata != null && msg.Metadata.ContainsKey("Timestamp") ? (DateTime)msg.Metadata["Timestamp"]! : DateTime.UtcNow,
                        OrderIndex = i
                    });
                }
            }
            catch { }
        }

        // 2. Backup: AgentCore
        try
        {
            string coreSql;
            if (_isCorePostgres)
            {
                coreSql = @"
                    INSERT INTO chat_sessions (id, user_phone, history_json, needs_human_attention, created_at, updated_at)
                    VALUES (@Id, @UserPhone, @Snapshot::jsonb, false, @CreatedAt, @UpdatedAt)
                    ON CONFLICT (id) DO UPDATE SET 
                        history_json = EXCLUDED.history_json, 
                        updated_at = EXCLUDED.updated_at";
            }
            else
            {
                coreSql = @"
                    IF NOT EXISTS (SELECT 1 FROM ChatSessions WHERE Id = @Id)
                    BEGIN
                        INSERT INTO ChatSessions (Id, TenantId, UserPhone, HistoryJson, NeedsHumanAttention, CreatedAt, UpdatedAt)
                        VALUES (@Id, @TenantId, @UserPhone, @Snapshot, 0, @CreatedAt, @UpdatedAt)
                    END
                    ELSE
                    BEGIN
                        UPDATE ChatSessions SET HistoryJson = @Snapshot, UpdatedAt = @UpdatedAt 
                        WHERE Id = @Id AND TenantId = @TenantId
                    END";
            }

            using var coreConnection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString);
            await coreConnection.ExecuteAsync(coreSql, new
            {
                Id = sessionId,
                TenantId = tenantId,
                UserPhone = userPhone ?? string.Empty,
                Snapshot = promptSnapshotJson,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        catch { }
    }

    public async Task SetNeedsHumanAttentionAsync(Guid sessionId, string tenantId, bool needsAttention)
    {
        // 1. Primary: Tenant DB (PostgreSQL)
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            const string tenantSql = "UPDATE chat_sessions SET needs_human_attention = @NeedsAttention, updated_at = @UpdatedAt WHERE id = @Id";
            using var connection = new NpgsqlConnection(_tenantConnectionString);
            await connection.ExecuteAsync(tenantSql, new
            {
                NeedsAttention = needsAttention,
                UpdatedAt = DateTime.UtcNow,
                Id = sessionId
            });
        }

        // 2. Backup: AgentCore - Fire & Forget
        _ = Task.Run(async () =>
        {
            try
            {
                string coreSql = _isCorePostgres
                    ? "UPDATE chat_sessions SET needs_human_attention = @NeedsAttention, updated_at = @UpdatedAt WHERE id = @Id"
                    : "UPDATE ChatSessions SET NeedsHumanAttention = @NeedsAttention, UpdatedAt = @UpdatedAt WHERE Id = @Id AND TenantId = @TenantId";

                using var connection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString);
                await connection.ExecuteAsync(coreSql, new
                {
                    NeedsAttention = _isCorePostgres ? (object)needsAttention : (object)(needsAttention ? 1 : 0),
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
        // Primary: Tenant DB (PostgreSQL)
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string sql = @"
                    SELECT id as Id, user_phone as UserPhone, needs_human_attention as NeedsHumanAttention, created_at as CreatedAt, updated_at as UpdatedAt 
                    FROM chat_sessions 
                    ORDER BY updated_at DESC";

                using var connection = new NpgsqlConnection(_tenantConnectionString);
                var sessions = await connection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(sql);
                return sessions.Select(s => { s.TenantId = tenantId; return s; }).ToList();
            }
            catch { }
        }

        // Fallback: AgentCore
        string coreSql = _isCorePostgres
            ? "SELECT id as Id, user_phone as UserPhone, needs_human_attention as NeedsHumanAttention, created_at as CreatedAt, updated_at as UpdatedAt FROM chat_sessions ORDER BY updated_at DESC"
            : "SELECT Id, TenantId, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt FROM ChatSessions WHERE TenantId = @TenantId ORDER BY UpdatedAt DESC";

        using var coreConnection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString);
        var coreSessions = await coreConnection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(coreSql, new { TenantId = tenantId });
        return coreSessions.Select(s => { if (_isCorePostgres) s.TenantId = tenantId; return s; }).ToList();
    }

    public async Task<List<ReceptionistAgent.Core.Models.ChatSessionDto>> GetSessionsByPhoneAsync(string tenantId, string phone)
    {
        // Primary: Tenant DB
        if (!string.IsNullOrEmpty(_tenantConnectionString))
        {
            try
            {
                const string sql = @"
                    SELECT id as Id, user_phone as UserPhone, needs_human_attention as NeedsHumanAttention, created_at as CreatedAt, updated_at as UpdatedAt 
                    FROM chat_sessions 
                    WHERE user_phone = @Phone
                    ORDER BY updated_at DESC";

                using var connection = new NpgsqlConnection(_tenantConnectionString);
                var sessions = await connection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(sql, new { Phone = phone });
                return sessions.Select(s => { s.TenantId = tenantId; return s; }).ToList();
            }
            catch { }
        }

        // Fallback: AgentCore
        string coreSql = _isCorePostgres
            ? "SELECT id as Id, user_phone as UserPhone, needs_human_attention as NeedsHumanAttention, created_at as CreatedAt, updated_at as UpdatedAt FROM chat_sessions WHERE user_phone = @Phone AND tenant_id = @TenantId ORDER BY updated_at DESC"
            : "SELECT Id, TenantId, UserPhone, NeedsHumanAttention, CreatedAt, UpdatedAt FROM ChatSessions WHERE TenantId = @TenantId AND UserPhone = @Phone ORDER BY UpdatedAt DESC";

        using var coreConnection = _isCorePostgres ? (System.Data.IDbConnection)new NpgsqlConnection(_agentCoreConnectionString) : new SqlConnection(_agentCoreConnectionString);
        var coreSessions = await coreConnection.QueryAsync<ReceptionistAgent.Core.Models.ChatSessionDto>(coreSql, new { TenantId = tenantId, Phone = phone });
        return coreSessions.Select(s => { if (_isCorePostgres) s.TenantId = tenantId; return s; }).ToList();
    }
}
