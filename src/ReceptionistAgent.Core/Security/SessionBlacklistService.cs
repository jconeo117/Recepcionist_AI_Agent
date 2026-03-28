using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;

namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Implementación de ISessionBlacklistService utilizando IMemoryCache.
/// </summary>
public class SessionBlacklistService : ISessionBlacklistService
{
    private readonly IMemoryCache _cache;
    private const string CachePrefix = "blacklist_";

    public SessionBlacklistService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<bool> IsBlacklistedAsync(Guid sessionId)
    {
        bool isBlacklisted = _cache.TryGetValue(GetCacheKey(sessionId), out _);
        return Task.FromResult(isBlacklisted);
    }

    public Task BlacklistSessionAsync(Guid sessionId, TimeSpan duration)
    {
        _cache.Set(GetCacheKey(sessionId), true, duration);
        return Task.CompletedTask;
    }

    private static string GetCacheKey(Guid sessionId) => $"{CachePrefix}{sessionId}";
}
