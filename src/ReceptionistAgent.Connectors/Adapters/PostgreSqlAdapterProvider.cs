using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Services;

namespace ReceptionistAgent.Connectors.Adapters;

/// <summary>
/// Estrategia para PostgreSQL.
/// </summary>
public class PostgreSqlAdapterProvider : IDataAdapterProvider
{
    public bool Supports(string dbType) => 
        string.Equals(dbType, "PostgreSql", StringComparison.OrdinalIgnoreCase);

    public IClientDataAdapter CreateAdapter(string connectionString, IBookingBackupService backupService, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<PostgreSqlClientDataAdapter>();
        return new PostgreSqlClientDataAdapter(connectionString, backupService, logger);
    }

    public IChatSessionRepository CreateChatSessionRepository(string coreConnectionString, string? tenantConnectionString, ILoggerFactory loggerFactory)
    {
        return new PostgreSqlChatSessionRepository(coreConnectionString, tenantConnectionString, loggerFactory);
    }

    public IReminderService CreateReminderService(string coreConnectionString, string? tenantConnectionString)
    {
        return new PostgreSqlReminderService(coreConnectionString, tenantConnectionString);
    }
}
