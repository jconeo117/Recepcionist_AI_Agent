using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// CRUD de tenants y gestión de billing.
/// Protegido con API Key.
/// </summary>
[ApiController]
[Route("api/admin/tenants")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class TenantAdminController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;
    private readonly IBillingService _billingService;

    public TenantAdminController(ITenantResolver tenantResolver, IBillingService billingService)
    {
        _tenantResolver = tenantResolver;
        _billingService = billingService;
    }

    // ═══ Tenant CRUD ═══

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenants = await _tenantResolver.GetAllTenantsAsync();
        return Ok(new { total = tenants.Count, tenants });
    }

    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetById(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        var billing = await _billingService.GetBillingAsync(tenantId);
        return Ok(new { tenant, billing });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TenantConfiguration tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.BusinessName))
            return BadRequest(new { error = "TenantId y BusinessName son requeridos." });

        var existing = await _tenantResolver.ResolveAsync(tenant.TenantId);
        if (existing != null)
            return Conflict(new { error = $"Tenant '{tenant.TenantId}' ya existe." });

        tenant.CreatedAt = DateTime.UtcNow;
        tenant.IsActive = true;
        var created = await _tenantResolver.CreateAsync(tenant);

        // Crear billing por defecto (Trial, 30 días)
        await _billingService.CreateBillingAsync(new TenantBilling
        {
            TenantId = tenant.TenantId,
            PlanType = PlanType.Trial,
            BillingStatus = BillingStatus.Active,
            ActiveUntil = DateTime.UtcNow.AddDays(30)
        });

        return CreatedAtAction(nameof(GetById), new { tenantId = created.TenantId }, created);
    }

    [HttpPut("{tenantId}")]
    public async Task<IActionResult> Update(string tenantId, [FromBody] TenantConfiguration tenant)
    {
        var existing = await _tenantResolver.ResolveAsync(tenantId);
        if (existing == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        tenant.TenantId = tenantId;
        tenant.UpdatedAt = DateTime.UtcNow;
        var updated = await _tenantResolver.UpdateAsync(tenant);
        return Ok(updated);
    }

    [HttpDelete("{tenantId}")]
    public async Task<IActionResult> Delete(string tenantId)
    {
        var success = await _tenantResolver.DeleteAsync(tenantId);
        if (!success)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        return Ok(new { message = $"Tenant '{tenantId}' desactivado." });
    }

    // ═══ Billing ═══

    [HttpGet("{tenantId}/billing")]
    public async Task<IActionResult> GetBilling(string tenantId)
    {
        var billing = await _billingService.GetBillingAsync(tenantId);
        if (billing == null)
            return NotFound(new { error = "No hay registro de billing para este tenant." });

        return Ok(billing);
    }

    [HttpPut("{tenantId}/billing")]
    public async Task<IActionResult> UpdateBilling(string tenantId, [FromBody] TenantBilling billing)
    {
        billing.TenantId = tenantId;
        await _billingService.UpdateBillingAsync(billing);
        return Ok(new { message = "Billing actualizado." });
    }

    [HttpPost("{tenantId}/suspend")]
    public async Task<IActionResult> Suspend(string tenantId, [FromBody] SuspendRequest request)
    {
        await _billingService.SuspendTenantAsync(tenantId, request.Reason ?? "Suspendido por administrador");
        return Ok(new { message = $"Tenant '{tenantId}' suspendido." });
    }

    [HttpPost("{tenantId}/reactivate")]
    public async Task<IActionResult> Reactivate(string tenantId, [FromBody] ReactivateRequest request)
    {
        var activeUntil = request.ActiveUntil ?? DateTime.UtcNow.AddDays(30);
        await _billingService.ReactivateTenantAsync(tenantId, activeUntil);
        return Ok(new { message = $"Tenant '{tenantId}' reactivado hasta {activeUntil:yyyy-MM-dd}." });
    }
}

public record SuspendRequest(string? Reason);
public record ReactivateRequest(DateTime? ActiveUntil);
