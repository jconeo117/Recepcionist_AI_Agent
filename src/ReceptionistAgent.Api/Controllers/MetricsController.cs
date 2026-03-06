using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Connectors.Repositories;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// Endpoint de métricas agregadas.
/// Protegido con API Key.
/// </summary>
[ApiController]
[Route("api/admin/metrics")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class MetricsController : ControllerBase
{
    private readonly SqlMetricsRepository _metricsRepository;

    public MetricsController(SqlMetricsRepository metricsRepository)
    {
        _metricsRepository = metricsRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] string? tenantId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var metrics = await _metricsRepository.GetMetricsAsync(tenantId, fromDate, toDate);
        return Ok(metrics);
    }
}
