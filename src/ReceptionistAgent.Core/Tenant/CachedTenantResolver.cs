using Microsoft.Extensions.Caching.Memory;
using ReceptionistAgent.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReceptionistAgent.Core.Tenant;

/// <summary>
/// Decorator para ITenantResolver que implementa caché en memoria.
/// Mejora el rendimiento al evitar consultas repetitivas a la base de datos para la configuración del tenant.
/// </summary>
public class CachedTenantResolver : ITenantResolver
{
    private readonly ITenantResolver _inner;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public CachedTenantResolver(ITenantResolver inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<TenantConfiguration?> ResolveAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) return null;

        string cacheKey = $"tenant_{tenantId.ToLower()}";

        if (!_cache.TryGetValue(cacheKey, out TenantConfiguration? config))
        {
            config = await _inner.ResolveAsync(tenantId);
            if (config != null)
            {
                _cache.Set(cacheKey, config, CacheDuration);
            }
        }

        return config;
    }

    public async Task<TenantConfiguration?> AuthenticateAsync(string username, string password)
    {
        // La autenticación no se cachea por seguridad y porque se hace una sola vez por sesión de admin.
        return await _inner.AuthenticateAsync(username, password);
    }

    public async Task<TenantConfiguration> CreateAsync(TenantConfiguration tenant)
    {
        var result = await _inner.CreateAsync(tenant);
        // No cacheamos aquí, el primer Resolve lo hará.
        return result;
    }

    public async Task<TenantConfiguration> UpdateAsync(TenantConfiguration tenant)
    {
        var result = await _inner.UpdateAsync(tenant);
        
        // Invalidar caché para asegurar que los cambios se reflejen de inmediato
        string cacheKey = $"tenant_{tenant.TenantId.ToLower()}";
        _cache.Remove(cacheKey);
        
        return result;
    }

    public async Task<bool> DeleteAsync(string tenantId)
    {
        var result = await _inner.DeleteAsync(tenantId);
        
        if (result)
        {
            string cacheKey = $"tenant_{tenantId.ToLower()}";
            _cache.Remove(cacheKey);
        }
        
        return result;
    }

    public Task<List<TenantConfiguration>> GetAllTenantsAsync()
    {
        // El listado general no se cachea para evitar inconsistencias en el panel de administración
        return _inner.GetAllTenantsAsync();
    }

    public Task<List<string>> GetAllTenantIdsAsync()
    {
        return _inner.GetAllTenantIdsAsync();
    }
}
