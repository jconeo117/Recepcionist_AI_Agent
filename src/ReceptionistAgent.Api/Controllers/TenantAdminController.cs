using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Connectors.Adapters;

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
    private readonly Connectors.Services.TenantDbInitializer _dbInitializer;
    private readonly ClientDataAdapterFactory _adapterFactory;

    public TenantAdminController(
        ITenantResolver tenantResolver, 
        IBillingService billingService,
        Connectors.Services.TenantDbInitializer dbInitializer,
        ClientDataAdapterFactory adapterFactory)
    {
        _tenantResolver = tenantResolver;
        _billingService = billingService;
        _dbInitializer = dbInitializer;
        _adapterFactory = adapterFactory;
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

        // Fetch live providers dynamically from the tenant's specific database instead of outdated core JSON
        if (tenant.DbType != "InMemory" && !string.IsNullOrWhiteSpace(tenant.ConnectionString))
        {
            try 
            {
                var adapter = _adapterFactory.CreateAdapter(tenant);
                var liveProviders = await adapter.GetAllProvidersAsync();
                tenant.Providers = liveProviders.Select(p => new TenantProviderConfig
                {
                    Id = p.Id,
                    Name = p.Name,
                    Role = p.Role,
                    StartTime = p.StartTime.ToString(@"hh\:mm"),
                    EndTime = p.EndTime.ToString(@"hh\:mm"),
                    SlotDurationMinutes = p.SlotDurationMinutes
                }).ToList();
            } 
            catch (Exception) 
            {
                // Fallback to empty or existing if DB is unreachable, so it doesn't break the whole admin panel
                tenant.Providers = new List<TenantProviderConfig>();
            }
        }

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

        // Aplicar defaults si el frontend no los envío
        if (tenant.BookingRequirements == null)
        {
            tenant.BookingRequirements = BookingRequirementsFactory.CreateFor(tenant.BusinessType);
        }

        if (tenant.ServiceModality == ServiceModality.InPerson)
        {
            // Solo sobreescribir si no fue explícitamente seteado por el admin,
            // usando un default inteligente según el tipo de negocio.
            tenant.ServiceModality = BookingRequirementsFactory.ModalityFor(tenant.BusinessType);
        }

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

        // Fase 2.4: Inicializar DB si es SqlServer
        string? initWarning = null;
        if (tenant.DbType == "SqlServer" && !string.IsNullOrWhiteSpace(tenant.ConnectionString))
        {
            try
            {
                await _dbInitializer.InitializeAsync(tenant.ConnectionString);
            }
            catch (Exception ex)
            {
                initWarning = $"Tenant creado pero falló la inicialización de la DB: {ex.Message}";
            }
        }

        if (initWarning != null)
        {
            return CreatedAtAction(nameof(GetById), new { tenantId = created.TenantId }, new { tenant = created, warning = initWarning });
        }

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

    [HttpPost("{tenantId}/reinitialize")]
    public async Task<IActionResult> ReinitializeDb(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        if (tenant.DbType != "SqlServer" || string.IsNullOrWhiteSpace(tenant.ConnectionString))
            return BadRequest(new { error = "Solo se puede inicializar la base de datos de tenants con DbType 'SqlServer' y ConnectionString configurada." });

        try
        {
            await _dbInitializer.InitializeAsync(tenant.ConnectionString);
            return Ok(new { message = "Base de datos inicializada correctamente." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Error al inicializar la base de datos: {ex.Message}" });
        }
    }
}

public record SuspendRequest(string? Reason);
public record ReactivateRequest(DateTime? ActiveUntil);
