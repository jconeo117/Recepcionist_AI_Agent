using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReceptionistAgent.Core.Services;
using System.Threading.Tasks;
using System;

namespace ReceptionistAgent.Connectors.Services;

public class PostgreSqlWebhookDeduplicator : IWebhookDeduplicator
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlWebhookDeduplicator> _logger;
    private bool _tableEnsured;

    public PostgreSqlWebhookDeduplicator(string connectionString, ILoggerFactory? loggerFactory = null)
    {
        _connectionString = connectionString;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<PostgreSqlWebhookDeduplicator>();
    }

    private async Task EnsureTableAsync()
    {
        if (_tableEnsured) return;
        
        const string sql = @"
            CREATE TABLE IF NOT EXISTS processed_webhooks (
                message_id VARCHAR PRIMARY KEY,
                tenant_id VARCHAR NOT NULL,
                processed_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );";
            
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(sql);
            _tableEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure processed_webhooks table exists.");
        }
    }

    public async Task<bool> TryRegisterMessageAsync(string messageId, string tenantId)
    {
        await EnsureTableAsync();
        
        const string sql = @"
            INSERT INTO processed_webhooks (message_id, tenant_id, processed_at)
            VALUES (@MessageId, @TenantId, @Now)
            ON CONFLICT (message_id) DO NOTHING;";
            
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            var affectedRows = await connection.ExecuteAsync(sql, new { 
                MessageId = messageId, 
                TenantId = tenantId,
                Now = DateTime.UtcNow
            });
            
            // If rows affected is > 0, the insert succeeded (it's new)
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering webhook message ID {MessageId}", messageId);
            // On fallback, return true to try to process it anyway
            return true;
        }
    }
}
