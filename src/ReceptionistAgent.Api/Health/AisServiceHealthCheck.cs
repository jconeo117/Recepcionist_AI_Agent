using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using ReceptionistAgent.AI.Configuration;

namespace ReceptionistAgent.Api.Health;

public class AisServiceHealthCheck : IHealthCheck
{
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _configuration;

    public AisServiceHealthCheck(KernelFactory kernelFactory, IConfiguration configuration)
    {
        _kernelFactory = kernelFactory;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_kernelFactory == null)
                return Task.FromResult(HealthCheckResult.Unhealthy("KernelFactory not configured"));

            var provider = _configuration["AI:Provider"] ?? "Google";
            // We just check if the factory can resolve the provider
            var settings = _kernelFactory.GetExecutionSettings(provider);
            
            if (settings == null)
                return Task.FromResult(HealthCheckResult.Unhealthy($"AI Provider '{provider}' not supported by factory"));

            return Task.FromResult(HealthCheckResult.Healthy($"AI service ({provider}) is configured"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("AI service configuration error", ex));
        }
    }
}
