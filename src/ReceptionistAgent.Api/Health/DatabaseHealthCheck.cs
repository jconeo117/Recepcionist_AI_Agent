using Microsoft.Extensions.Diagnostics.HealthChecks;
using Dapper;
using Npgsql;
using Microsoft.Data.SqlClient;

namespace ReceptionistAgent.Api.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public DatabaseHealthCheck(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isPostgres = _connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
            
            using System.Data.IDbConnection connection = isPostgres 
                ? new NpgsqlConnection(_connectionString) 
                : new SqlConnection(_connectionString);

            await connection.QueryAsync("SELECT 1");
            return HealthCheckResult.Healthy("Database is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable", ex);
        }
    }
}
