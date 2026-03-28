using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class ProviderController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;
    private readonly ClientDataAdapterFactory _adapterFactory;
    private readonly ILogger<ProviderController> _logger;

    public ProviderController(
        ITenantResolver tenantResolver,
        ClientDataAdapterFactory adapterFactory,
        ILogger<ProviderController> logger)
    {
        _tenantResolver = tenantResolver;
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders()
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var providers = await adapter.GetAllProvidersAsync();
        return Ok(providers);
    }

    [HttpPost("providers")]
    public async Task<IActionResult> CreateProvider([FromBody] ReceptionistAgent.Core.Models.ServiceProvider provider)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var success = await adapter.AddProviderAsync(provider);
        
        if (!success) return StatusCode(500, "Error creating provider in the database.");
        return Ok(provider);
    }

    [HttpPut("providers/{id}")]
    public async Task<IActionResult> UpdateProvider(string id, [FromBody] ReceptionistAgent.Core.Models.ServiceProvider provider)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        if (id != provider.Id) return BadRequest("ID mismatch");

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var success = await adapter.UpdateProviderAsync(provider);

        if (!success) return NotFound("Provider not found or could not be updated.");
        return Ok(provider);
    }

    [HttpDelete("providers/{id}")]
    public async Task<IActionResult> DeleteProvider(string id)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var success = await adapter.DeleteProviderAsync(id);

        if (!success) return NotFound("Provider not found or could not be deleted.");
        return Ok(new { success = true });
    }
}
