using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Services;

namespace ReceptionistAgent.Connectors.Adapters;

/// <summary>
/// Estrategia para SQL Server.
/// </summary>
public class SqlServerAdapterProvider : IDataAdapterProvider
{
    public bool Supports(string dbType) => 
        string.Equals(dbType, "SqlServer", StringComparison.OrdinalIgnoreCase);

    public IClientDataAdapter CreateAdapter(string connectionString, IBookingBackupService backupService, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<SqlClientDataAdapter>();
        return new SqlClientDataAdapter(connectionString, backupService, logger);
    }

    public IChatSessionRepository CreateChatSessionRepository(string coreConnectionString, string? tenantConnectionString, ILoggerFactory loggerFactory)
    {
        return new SqlChatSessionRepository(coreConnectionString, tenantConnectionString);
    }

    public IReminderService CreateReminderService(string coreConnectionString, string? tenantConnectionString)
    {
        return new SqlReminderService(coreConnectionString, tenantConnectionString);
    }
}
