using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Repositories;
using ReceptionistAgent.Core.Tenant;
using System.Security.Claims;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// Endpoint de exploración de base de datos de tenants.
/// Protegido con API Key.
/// </summary>
[ApiController]
[Route("api/admin/tenants/{tenantId}/database")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class DatabaseExplorerController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;
    private readonly IDatabaseAdminRepository _dbRepository;

    public DatabaseExplorerController(ITenantResolver tenantResolver, IDatabaseAdminRepository dbRepository)
    {
        _tenantResolver = tenantResolver;
        _dbRepository = dbRepository;
    }

    /// <summary>
    /// Obtiene el esquema de las tablas del tenant.
    /// </summary>
    [HttpGet("schema")]
    public async Task<IActionResult> GetSchema(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        try
        {
            var tables = await _dbRepository.GetTablesAsync(tenant);
            var bookingsColumns = await _dbRepository.GetTableColumnsAsync(tenant, "Bookings");

            return Ok(new
            {
                tenantId,
                dbType = tenant.DbType,
                tables,
                bookingsSchema = bookingsColumns
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Error explorando esquema: {ex.Message}" });
        }
    }

    /// <summary>
    /// Obtiene los bookings del tenant directamente desde su base de datos.
    /// </summary>
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings(string tenantId, [FromQuery] int limit = 50)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        try
        {
            var bookings = await _dbRepository.GetRawBookingsAsync(tenant, limit);
            var totalCount = await _dbRepository.GetTotalBookingsCountAsync(tenant);

            return Ok(new
            {
                tenantId,
                totalBookings = totalCount,
                showing = bookings.Count,
                bookings
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Error consultando bookings: {ex.Message}" });
        }
    }

    /// <summary>
    /// Obtiene un resumen del estado de las tablas principales en la DB del tenant.
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        try
        {
            var health = await _dbRepository.GetDatabaseHealthAsync(tenant);

            return Ok(new
            {
                tenantId,
                timestamp = DateTime.UtcNow,
                health
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Error consultando salud de DB: {ex.Message}" });
        }
    }
}
