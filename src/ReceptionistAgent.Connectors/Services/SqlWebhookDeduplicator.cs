using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReceptionistAgent.Core.Services;
using System.Threading.Tasks;
using System;

namespace ReceptionistAgent.Connectors.Services;

public class SqlWebhookDeduplicator : IWebhookDeduplicator
{
    private readonly string _connectionString;
    private readonly ILogger<SqlWebhookDeduplicator> _logger;
    private bool _tableEnsured;

    public SqlWebhookDeduplicator(string connectionString, ILoggerFactory? loggerFactory = null)
    {
        _connectionString = connectionString;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SqlWebhookDeduplicator>();
    }

    private async Task EnsureTableAsync()
    {
        if (_tableEnsured) return;
        
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ProcessedWebhooks' AND xtype='U')
            BEGIN
                CREATE TABLE ProcessedWebhooks (
                    MessageId NVARCHAR(255) PRIMARY KEY,
                    TenantId NVARCHAR(255) NOT NULL,
                    ProcessedAt DATETIME2 DEFAULT GETUTCDATE()
                )
            END";
            
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(sql);
            _tableEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure ProcessedWebhooks table exists.");
        }
    }

    public async Task<bool> TryRegisterMessageAsync(string messageId, string tenantId)
    {
        await EnsureTableAsync();
        
        const string sql = @"
            BEGIN TRY
                INSERT INTO ProcessedWebhooks (MessageId, TenantId, ProcessedAt)
                VALUES (@MessageId, @TenantId, @Now);
                SELECT 1;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() = 2627 OR ERROR_NUMBER() = 2601
                    SELECT 0;
                ELSE
                    THROW;
            END CATCH";
            
        try
        {
            using var connection = new SqlConnection(_connectionString);
            var result = await connection.QuerySingleOrDefaultAsync<int?>(sql, new { 
                MessageId = messageId, 
                TenantId = tenantId,
                Now = DateTime.UtcNow
            });
            
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering webhook message ID {MessageId}", messageId);
            return true;
        }
    }
}
