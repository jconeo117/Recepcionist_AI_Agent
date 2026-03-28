using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http;

namespace ReceptionistAgent.Api.Health;

public class MetaApiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MetaApiHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("MetaGraphApi");
            var response = await client.GetAsync("https://graph.facebook.com/v18.0/debug_token", cancellationToken);
            
            // We just check connectivity. Even a 400/401 means the API is reachable.
            // A timeout or DNS error would throw.
            return HealthCheckResult.Healthy("Meta API reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Meta API unreachable", ex);
        }
    }
}
