using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Tenant;

/// <summary>
/// Resuelve la configuración de un tenant a partir de su ID.
/// Diseñado para ser extensible: la implementación puede leer
/// de appsettings, base de datos, API externa, etc.
/// </summary>
public interface ITenantResolver
{
    Task<TenantConfiguration?> ResolveAsync(string tenantId);
    Task<List<string>> GetAllTenantIdsAsync();

    // Auth for Client Dashboard
    Task<TenantConfiguration?> AuthenticateAsync(string username, string password);

    // CRUD — supported by SqlTenantRepository
    Task<TenantConfiguration> CreateAsync(TenantConfiguration tenant);
    Task<TenantConfiguration> UpdateAsync(TenantConfiguration tenant);
    Task<bool> DeleteAsync(string tenantId);
    Task<List<TenantConfiguration>> GetAllTenantsAsync();
}
