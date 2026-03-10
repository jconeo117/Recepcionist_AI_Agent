using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Tenant;

/// <summary>
/// Implementación en memoria de ITenantResolver.
/// Carga configuraciones de tenants desde un diccionario inyectado por constructor.
/// 
/// Para migrar a base de datos, simplemente crear una nueva implementación
/// de ITenantResolver que lea de EF Core, Dapper, etc.
/// </summary>
public class InMemoryTenantResolver : ITenantResolver
{
    private readonly Dictionary<string, TenantConfiguration> _tenants;

    public InMemoryTenantResolver(Dictionary<string, TenantConfiguration> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        // Ensure case-insensitive lookups regardless of input dictionary's comparer
        _tenants = new Dictionary<string, TenantConfiguration>(tenants, StringComparer.OrdinalIgnoreCase);
    }

    public Task<TenantConfiguration?> ResolveAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return Task.FromResult<TenantConfiguration?>(null);

        // Dictionary was created with StringComparer.OrdinalIgnoreCase, so lookup is O(1)
        return _tenants.TryGetValue(tenantId, out var tenant)
            ? Task.FromResult<TenantConfiguration?>(tenant)
            : Task.FromResult<TenantConfiguration?>(null);
    }

    public Task<List<string>> GetAllTenantIdsAsync()
    {
        return Task.FromResult(_tenants.Keys.ToList());
    }

    public Task<TenantConfiguration?> AuthenticateAsync(string username, string password)
    {
        var tenant = _tenants.Values.FirstOrDefault(t =>
            string.Equals(t.Username, username, StringComparison.OrdinalIgnoreCase) &&
            t.PasswordHash == password);
        return Task.FromResult(tenant);
    }

    // CRUD — not supported in-memory (use SqlTenantRepository for production)
    public Task<TenantConfiguration> CreateAsync(TenantConfiguration tenant)
        => throw new NotSupportedException("InMemoryTenantResolver does not support CRUD. Use SqlTenantRepository.");

    public Task<TenantConfiguration> UpdateAsync(TenantConfiguration tenant)
        => throw new NotSupportedException("InMemoryTenantResolver does not support CRUD. Use SqlTenantRepository.");

    public Task<bool> DeleteAsync(string tenantId)
        => throw new NotSupportedException("InMemoryTenantResolver does not support CRUD. Use SqlTenantRepository.");

    public Task<List<TenantConfiguration>> GetAllTenantsAsync()
    {
        return Task.FromResult(_tenants.Values.ToList());
    }
}
