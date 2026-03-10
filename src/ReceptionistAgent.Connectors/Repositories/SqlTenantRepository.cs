using Dapper;
using Microsoft.Data.SqlClient;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using System.Text.Json;

namespace ReceptionistAgent.Connectors.Repositories;

/// <summary>
/// Implementación de ITenantResolver respaldada por SQL Server.
/// Reemplaza a InMemoryTenantResolver para producción.
/// Lee de tablas Tenants + TenantProviders.
/// </summary>
public class SqlTenantRepository : ITenantResolver
{
    private readonly string _connectionString;

    public SqlTenantRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<TenantConfiguration?> ResolveAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        const string tenantSql = "SELECT * FROM Tenants WHERE TenantId = @TenantId AND IsActive = 1";
        const string providersSql = "SELECT * FROM TenantProviders WHERE TenantId = @TenantId AND IsActive = 1";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<TenantEntity>(tenantSql, new { TenantId = tenantId });

        if (entity == null)
            return null;

        var providerEntities = (await connection.QueryAsync<ProviderEntity>(providersSql, new { TenantId = tenantId })).ToList();

        return MapToConfiguration(entity, providerEntities);
    }

    public async Task<List<string>> GetAllTenantIdsAsync()
    {
        const string sql = "SELECT TenantId FROM Tenants WHERE IsActive = 1";
        using var connection = new SqlConnection(_connectionString);
        var ids = await connection.QueryAsync<string>(sql);
        return ids.ToList();
    }

    public async Task<TenantConfiguration?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        const string tenantSql = "SELECT * FROM Tenants WHERE Username = @Username AND PasswordHash = @PasswordHash AND IsActive = 1";
        const string providersSql = "SELECT * FROM TenantProviders WHERE TenantId = @TenantId AND IsActive = 1";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<TenantEntity>(tenantSql, new { Username = username, PasswordHash = password });

        if (entity == null)
            return null;

        var providerEntities = (await connection.QueryAsync<ProviderEntity>(providersSql, new { TenantId = entity.TenantId })).ToList();

        return MapToConfiguration(entity, providerEntities);
    }

    public async Task<List<TenantConfiguration>> GetAllTenantsAsync()
    {
        const string tenantSql = "SELECT * FROM Tenants WHERE IsActive = 1";
        const string providersSql = "SELECT * FROM TenantProviders WHERE IsActive = 1";

        using var connection = new SqlConnection(_connectionString);
        var entities = (await connection.QueryAsync<TenantEntity>(tenantSql)).ToList();
        var allProviders = (await connection.QueryAsync<ProviderEntity>(providersSql)).ToList();

        return entities.Select(e =>
        {
            var providers = allProviders.Where(p => p.TenantId == e.TenantId).ToList();
            return MapToConfiguration(e, providers);
        }).ToList();
    }

    public async Task<TenantConfiguration> CreateAsync(TenantConfiguration tenant)
    {
        const string sql = @"
            INSERT INTO Tenants (TenantId, BusinessName, BusinessType, DbType, ConnectionString,
                TimeZoneId, Address, Phone, WorkingHours, Services, AcceptedInsurance,
                Pricing, CustomSettings, Username, PasswordHash, IsActive, CreatedAt)
            VALUES (@TenantId, @BusinessName, @BusinessType, @DbType, @ConnectionString,
                @TimeZoneId, @Address, @Phone, @WorkingHours, @Services, @AcceptedInsurance,
                @Pricing, @CustomSettings, @Username, @PasswordHash, @IsActive, @CreatedAt)";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            tenant.TenantId,
            tenant.BusinessName,
            tenant.BusinessType,
            tenant.DbType,
            tenant.ConnectionString,
            tenant.TimeZoneId,
            tenant.Address,
            tenant.Phone,
            tenant.WorkingHours,
            Services = JsonSerializer.Serialize(tenant.Services),
            AcceptedInsurance = JsonSerializer.Serialize(tenant.AcceptedInsurance),
            Pricing = JsonSerializer.Serialize(tenant.Pricing),
            CustomSettings = JsonSerializer.Serialize(tenant.CustomSettings),
            Username = tenant.Username,
            PasswordHash = tenant.PasswordHash,
            tenant.IsActive,
            tenant.CreatedAt
        });

        // Insert providers
        foreach (var provider in tenant.Providers)
        {
            await InsertProviderAsync(connection, tenant.TenantId, provider);
        }

        return tenant;
    }

    public async Task<TenantConfiguration> UpdateAsync(TenantConfiguration tenant)
    {
        const string sql = @"
            UPDATE Tenants SET
                BusinessName = @BusinessName, BusinessType = @BusinessType,
                DbType = @DbType, ConnectionString = @ConnectionString,
                TimeZoneId = @TimeZoneId, Address = @Address, Phone = @Phone,
                WorkingHours = @WorkingHours, Services = @Services,
                AcceptedInsurance = @AcceptedInsurance, Pricing = @Pricing,
                CustomSettings = @CustomSettings, Username = @Username, PasswordHash = @PasswordHash,
                IsActive = @IsActive, UpdatedAt = @UpdatedAt
            WHERE TenantId = @TenantId";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            tenant.TenantId,
            tenant.BusinessName,
            tenant.BusinessType,
            tenant.DbType,
            tenant.ConnectionString,
            tenant.TimeZoneId,
            tenant.Address,
            tenant.Phone,
            tenant.WorkingHours,
            Services = JsonSerializer.Serialize(tenant.Services),
            AcceptedInsurance = JsonSerializer.Serialize(tenant.AcceptedInsurance),
            Pricing = JsonSerializer.Serialize(tenant.Pricing),
            CustomSettings = JsonSerializer.Serialize(tenant.CustomSettings),
            Username = tenant.Username,
            PasswordHash = tenant.PasswordHash,
            tenant.IsActive,
            UpdatedAt = DateTime.UtcNow
        });

        // Replace providers: delete existing, insert new
        await connection.ExecuteAsync(
            "DELETE FROM TenantProviders WHERE TenantId = @TenantId",
            new { tenant.TenantId });

        foreach (var provider in tenant.Providers)
        {
            await InsertProviderAsync(connection, tenant.TenantId, provider);
        }

        return tenant;
    }

    public async Task<bool> DeleteAsync(string tenantId)
    {
        // Soft delete
        const string sql = "UPDATE Tenants SET IsActive = 0, UpdatedAt = @Now WHERE TenantId = @TenantId";
        using var connection = new SqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(sql, new { TenantId = tenantId, Now = DateTime.UtcNow });
        return rows > 0;
    }

    // ────────────────────── Private Helpers ──────────────────────

    private static async Task InsertProviderAsync(SqlConnection connection, string tenantId, TenantProviderConfig provider)
    {
        const string sql = @"
            INSERT INTO TenantProviders (Id, TenantId, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive)
            VALUES (@Id, @TenantId, @Name, @Role, @WorkingDays, @StartTime, @EndTime, @SlotDurationMin, 1)";

        await connection.ExecuteAsync(sql, new
        {
            provider.Id,
            TenantId = tenantId,
            provider.Name,
            provider.Role,
            WorkingDays = JsonSerializer.Serialize(provider.WorkingDays),
            provider.StartTime,
            provider.EndTime,
            SlotDurationMin = provider.SlotDurationMinutes
        });
    }

    private static TenantConfiguration MapToConfiguration(TenantEntity entity, List<ProviderEntity> providers)
    {
        return new TenantConfiguration
        {
            TenantId = entity.TenantId,
            BusinessName = entity.BusinessName,
            BusinessType = entity.BusinessType,
            DbType = entity.DbType ?? "InMemory",
            ConnectionString = entity.ConnectionString ?? "",
            TimeZoneId = entity.TimeZoneId ?? "UTC",
            Address = entity.Address ?? "",
            Phone = entity.Phone ?? "",
            WorkingHours = entity.WorkingHours ?? "",
            Services = DeserializeJson<List<string>>(entity.Services) ?? [],
            AcceptedInsurance = DeserializeJson<List<string>>(entity.AcceptedInsurance) ?? [],
            Pricing = DeserializeJson<Dictionary<string, string>>(entity.Pricing) ?? new(),
            CustomSettings = DeserializeJson<Dictionary<string, object>>(entity.CustomSettings) ?? new(),
            Username = entity.Username,
            PasswordHash = entity.PasswordHash,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Providers = providers.Select(p => new TenantProviderConfig
            {
                Id = p.Id,
                Name = p.Name,
                Role = p.Role,
                WorkingDays = DeserializeJson<List<string>>(p.WorkingDays) ?? [],
                StartTime = p.StartTime ?? "09:00",
                EndTime = p.EndTime ?? "18:00",
                SlotDurationMinutes = p.SlotDurationMin
            }).ToList()
        };
    }

    private static T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return default; }
    }

    // ────────────────────── Dapper Entities ──────────────────────

    private class TenantEntity
    {
        public string TenantId { get; set; } = "";
        public string BusinessName { get; set; } = "";
        public string BusinessType { get; set; } = "";
        public string? DbType { get; set; }
        public string? ConnectionString { get; set; }
        public string? TimeZoneId { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? WorkingHours { get; set; }
        public string? Services { get; set; }
        public string? AcceptedInsurance { get; set; }
        public string? Pricing { get; set; }
        public string? CustomSettings { get; set; }
        public string? Username { get; set; }
        public string? PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private class ProviderEntity
    {
        public string Id { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string? WorkingDays { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public int SlotDurationMin { get; set; }
        public bool IsActive { get; set; }
    }
}
