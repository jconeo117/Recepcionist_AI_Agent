using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// CRUD de providers por tenant.
/// Protegido con API Key.
/// </summary>
[ApiController]
[Route("api/admin/tenants/{tenantId}/providers")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class ProviderAdminController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;

    public ProviderAdminController(ITenantResolver tenantResolver)
    {
        _tenantResolver = tenantResolver;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        return Ok(new { tenantId, total = tenant.Providers.Count, providers = tenant.Providers });
    }

    [HttpGet("{providerId}")]
    public async Task<IActionResult> GetById(string tenantId, string providerId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        var provider = tenant.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
            return NotFound(new { error = $"Provider '{providerId}' no encontrado en tenant '{tenantId}'." });

        return Ok(provider);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string tenantId, [FromBody] TenantProviderConfig provider)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        if (string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.Name))
            return BadRequest(new { error = "Id y Name del provider son requeridos." });

        if (tenant.Providers.Any(p => p.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = $"Provider '{provider.Id}' ya existe en tenant '{tenantId}'." });

        tenant.Providers.Add(provider);
        await _tenantResolver.UpdateAsync(tenant);

        return CreatedAtAction(nameof(GetById), new { tenantId, providerId = provider.Id }, provider);
    }

    [HttpPut("{providerId}")]
    public async Task<IActionResult> Update(string tenantId, string providerId, [FromBody] TenantProviderConfig provider)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        var existingIndex = tenant.Providers.FindIndex(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
            return NotFound(new { error = $"Provider '{providerId}' no encontrado." });

        provider.Id = providerId;
        tenant.Providers[existingIndex] = provider;
        await _tenantResolver.UpdateAsync(tenant);

        return Ok(provider);
    }

    [HttpDelete("{providerId}")]
    public async Task<IActionResult> Delete(string tenantId, string providerId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        var removed = tenant.Providers.RemoveAll(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return NotFound(new { error = $"Provider '{providerId}' no encontrado." });

        await _tenantResolver.UpdateAsync(tenant);
        return Ok(new { message = $"Provider '{providerId}' eliminado de tenant '{tenantId}'." });
    }
}
