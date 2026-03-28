using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.Core.Repositories;

/// <summary>
/// Repositorio para operaciones administrativas y de exploración de base de datos
/// que no encajen en el IClientDataAdapter (como inspección de esquemas o chats crudos).
/// </summary>
public interface IDatabaseAdminRepository
{
    Task<List<TableInfo>> GetTablesAsync(TenantConfiguration tenant);
    Task<List<ColumnInfo>> GetTableColumnsAsync(TenantConfiguration tenant, string tableName);
    Task<IEnumerable<dynamic>> GetRecentChatMessagesAsync(TenantConfiguration tenant, int limit = 50);
    Task<List<dynamic>> GetRawBookingsAsync(TenantConfiguration tenant, int limit = 50);
    Task<int> GetTotalBookingsCountAsync(TenantConfiguration tenant);
    Task<Dictionary<string, object>> GetDatabaseHealthAsync(TenantConfiguration tenant);
}
