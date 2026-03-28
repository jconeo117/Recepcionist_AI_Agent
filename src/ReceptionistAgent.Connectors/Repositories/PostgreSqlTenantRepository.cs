using Dapper;
using Npgsql;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using System.Text.Json;

namespace ReceptionistAgent.Connectors.Repositories;

public class PostgreSqlTenantRepository : ITenantResolver
{
    private readonly string _connectionString;

    public PostgreSqlTenantRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<TenantConfiguration?> ResolveAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        const string tenantSql = "SELECT tenant_id AS \"TenantId\", business_name AS \"BusinessName\", business_type AS \"BusinessType\", db_type AS \"DbType\", connection_string AS \"ConnectionString\", time_zone_id AS \"TimeZoneId\", phone_country_code AS \"PhoneCountryCode\", address AS \"Address\", phone AS \"Phone\", working_hours AS \"WorkingHours\", services AS \"Services\", accepted_insurance AS \"AcceptedInsurance\", pricing AS \"Pricing\", custom_settings AS \"CustomSettings\", username AS \"Username\", password_hash AS \"PasswordHash\", message_provider AS \"MessageProvider\", message_provider_account AS \"MessageProviderAccount\", message_provider_token AS \"MessageProviderToken\", message_provider_phone AS \"MessageProviderPhone\", webhook_url AS \"WebhookUrl\", booking_requirements_json AS \"BookingRequirementsJson\", service_modality AS \"ServiceModality\", is_active AS \"IsActive\", created_at AS \"CreatedAt\", updated_at AS \"UpdatedAt\" FROM tenants WHERE tenant_id = @TenantId AND is_active = TRUE";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<TenantEntity>(tenantSql, new { TenantId = tenantId });
        if (entity == null) return null;

        var providerEntities = new List<ProviderEntity>();

        return MapToConfiguration(entity, providerEntities);
    }

    public async Task<TenantConfiguration?> ResolveByMetaPhoneNumberIdAsync(string phoneNumberId)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return null;

        const string tenantSql = @"
            SELECT tenant_id AS ""TenantId"", business_name AS ""BusinessName"", business_type AS ""BusinessType"", db_type AS ""DbType"", connection_string AS ""ConnectionString"", time_zone_id AS ""TimeZoneId"", phone_country_code AS ""PhoneCountryCode"", address AS ""Address"", phone AS ""Phone"", working_hours AS ""WorkingHours"", services AS ""Services"", accepted_insurance AS ""AcceptedInsurance"", pricing AS ""Pricing"", custom_settings AS ""CustomSettings"", username AS ""Username"", password_hash AS ""PasswordHash"", message_provider AS ""MessageProvider"", message_provider_account AS ""MessageProviderAccount"", message_provider_token AS ""MessageProviderToken"", message_provider_phone AS ""MessageProviderPhone"", webhook_url AS ""WebhookUrl"", booking_requirements_json AS ""BookingRequirementsJson"", service_modality AS ""ServiceModality"", is_active AS ""IsActive"", created_at AS ""CreatedAt"", updated_at AS ""UpdatedAt""
            FROM tenants
            WHERE message_provider_account = @PhoneNumberId
              AND message_provider = 'Meta'
              AND is_active = TRUE";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<TenantEntity>(tenantSql, new { PhoneNumberId = phoneNumberId });
        if (entity == null) return null;

        var providerEntities = new List<ProviderEntity>();

        return MapToConfiguration(entity, providerEntities);
    }

    public async Task<List<string>> GetAllTenantIdsAsync()
    {
        const string sql = "SELECT tenant_id FROM tenants WHERE is_active = TRUE";
        using var connection = new NpgsqlConnection(_connectionString);
        return (await connection.QueryAsync<string>(sql)).ToList();
    }

    public async Task<TenantConfiguration?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        const string tenantSql = "SELECT tenant_id AS \"TenantId\", business_name AS \"BusinessName\", business_type AS \"BusinessType\", db_type AS \"DbType\", connection_string AS \"ConnectionString\", time_zone_id AS \"TimeZoneId\", phone_country_code AS \"PhoneCountryCode\", address AS \"Address\", phone AS \"Phone\", working_hours AS \"WorkingHours\", services AS \"Services\", accepted_insurance AS \"AcceptedInsurance\", pricing AS \"Pricing\", custom_settings AS \"CustomSettings\", username AS \"Username\", password_hash AS \"PasswordHash\", message_provider AS \"MessageProvider\", message_provider_account AS \"MessageProviderAccount\", message_provider_token AS \"MessageProviderToken\", message_provider_phone AS \"MessageProviderPhone\", webhook_url AS \"WebhookUrl\", booking_requirements_json AS \"BookingRequirementsJson\", service_modality AS \"ServiceModality\", is_active AS \"IsActive\", created_at AS \"CreatedAt\", updated_at AS \"UpdatedAt\" FROM tenants WHERE username = @Username AND password_hash = @PasswordHash AND is_active = TRUE";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<TenantEntity>(tenantSql, new { Username = username, PasswordHash = password });
        if (entity == null) return null;

        var providerEntities = new List<ProviderEntity>();

        return MapToConfiguration(entity, providerEntities);
    }

    public async Task<List<TenantConfiguration>> GetAllTenantsAsync()
    {
        const string tenantSql = "SELECT tenant_id AS \"TenantId\", business_name AS \"BusinessName\", business_type AS \"BusinessType\", db_type AS \"DbType\", connection_string AS \"ConnectionString\", time_zone_id AS \"TimeZoneId\", phone_country_code AS \"PhoneCountryCode\", address AS \"Address\", phone AS \"Phone\", working_hours AS \"WorkingHours\", services AS \"Services\", accepted_insurance AS \"AcceptedInsurance\", pricing AS \"Pricing\", custom_settings AS \"CustomSettings\", username AS \"Username\", password_hash AS \"PasswordHash\", message_provider AS \"MessageProvider\", message_provider_account AS \"MessageProviderAccount\", message_provider_token AS \"MessageProviderToken\", message_provider_phone AS \"MessageProviderPhone\", webhook_url AS \"WebhookUrl\", booking_requirements_json AS \"BookingRequirementsJson\", service_modality AS \"ServiceModality\", is_active AS \"IsActive\", created_at AS \"CreatedAt\", updated_at AS \"UpdatedAt\" FROM tenants WHERE is_active = TRUE";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = (await connection.QueryAsync<TenantEntity>(tenantSql)).ToList();

        return entities.Select(e =>
        {
            var providers = new List<ProviderEntity>();
            return MapToConfiguration(e, providers);
        }).ToList();
    }

    public async Task<TenantConfiguration> CreateAsync(TenantConfiguration tenant)
    {
        const string sql = @"
            INSERT INTO tenants (
                tenant_id, business_name, business_type, db_type, connection_string,
                time_zone_id, phone_country_code, address, phone, working_hours,
                services, accepted_insurance, pricing, custom_settings,
                username, password_hash,
                message_provider, message_provider_account, message_provider_token, message_provider_phone,
                webhook_url,
                booking_requirements_json, service_modality,
                is_active, created_at, updated_at
            ) VALUES (
                @TenantId, @BusinessName, @BusinessType, @DbType, @ConnectionString,
                @TimeZoneId, @PhoneCountryCode, @Address, @Phone, @WorkingHours,
                CAST(@Services AS jsonb), CAST(@AcceptedInsurance AS jsonb), CAST(@Pricing AS jsonb), CAST(@CustomSettings AS jsonb),
                @Username, @PasswordHash,
                @MessageProvider, @MessageProviderAccount, @MessageProviderToken, @MessageProviderPhone,
                @WebhookUrl,
                CAST(@BookingRequirementsJson AS jsonb), @ServiceModality,
                @IsActive, @CreatedAt, @UpdatedAt
            )";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, BuildParams(tenant));


        return tenant;
    }

    public async Task<TenantConfiguration> UpdateAsync(TenantConfiguration tenant)
    {
        const string sql = @"
            UPDATE tenants SET
                business_name           = @BusinessName,
                business_type           = @BusinessType,
                db_type                 = @DbType,
                connection_string       = @ConnectionString,
                time_zone_id             = @TimeZoneId,
                phone_country_code       = @PhoneCountryCode,
                address                = @Address,
                phone                  = @Phone,
                working_hours           = @WorkingHours,
                services               = CAST(@Services AS jsonb),
                accepted_insurance      = CAST(@AcceptedInsurance AS jsonb),
                pricing                = CAST(@Pricing AS jsonb),
                custom_settings         = CAST(@CustomSettings AS jsonb),
                username               = @Username,
                password_hash           = @PasswordHash,
                message_provider        = @MessageProvider,
                message_provider_account = @MessageProviderAccount,
                message_provider_token   = @MessageProviderToken,
                message_provider_phone   = @MessageProviderPhone,
                webhook_url             = @WebhookUrl,
                booking_requirements_json= CAST(@BookingRequirementsJson AS jsonb),
                service_modality        = @ServiceModality,
                is_active               = @IsActive,
                updated_at              = @UpdatedAt
            WHERE tenant_id = @TenantId";

        var parameters = BuildParams(tenant);
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, parameters);

        return tenant;
    }

    public async Task<bool> DeleteAsync(string tenantId)
    {
        const string sql = "UPDATE tenants SET is_active = FALSE, updated_at = @Now WHERE tenant_id = @TenantId";
        using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(sql, new { TenantId = tenantId, Now = DateTime.UtcNow });
        return rows > 0;
    }

    private static object BuildParams(TenantConfiguration t) => new
    {
        t.TenantId,
        t.BusinessName,
        t.BusinessType,
        t.DbType,
        t.ConnectionString,
        t.TimeZoneId,
        t.PhoneCountryCode,
        t.Address,
        t.Phone,
        t.WorkingHours,
        Services = JsonSerializer.Serialize(t.Services),
        AcceptedInsurance = JsonSerializer.Serialize(t.AcceptedInsurance),
        Pricing = JsonSerializer.Serialize(t.Pricing),
        CustomSettings = JsonSerializer.Serialize(t.CustomSettings),
        t.Username,
        t.PasswordHash,
        t.MessageProvider,
        t.MessageProviderAccount,
        t.MessageProviderToken,
        t.MessageProviderPhone,
        t.WebhookUrl,
        BookingRequirementsJson = JsonSerializer.Serialize(t.BookingRequirements),
        ServiceModality = t.ServiceModality.ToString(),
        IsActive = t.IsActive,
        t.CreatedAt,
        UpdatedAt = DateTime.UtcNow
    };



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
            PhoneCountryCode = entity.PhoneCountryCode ?? "",
            Address = entity.Address ?? "",
            Phone = entity.Phone ?? "",
            WorkingHours = entity.WorkingHours ?? "",
            Services = DeserializeJson<List<string>>(entity.Services) ?? [],
            AcceptedInsurance = DeserializeJson<List<string>>(entity.AcceptedInsurance) ?? [],
            Pricing = DeserializeJson<Dictionary<string, string>>(entity.Pricing) ?? new(),
            CustomSettings = DeserializeJson<Dictionary<string, object>>(entity.CustomSettings) ?? new(),
            Username = entity.Username,
            PasswordHash = entity.PasswordHash,
            MessageProvider = entity.MessageProvider ?? "Meta",
            MessageProviderAccount = entity.MessageProviderAccount ?? "",
            MessageProviderToken = entity.MessageProviderToken ?? "",
            MessageProviderPhone = entity.MessageProviderPhone ?? "",
            WebhookUrl = entity.WebhookUrl,
            BookingRequirements = DeserializeJson<BookingRequirements>(entity.BookingRequirementsJson) ?? BookingRequirementsFactory.CreateFor(entity.BusinessType),
            ServiceModality = Enum.TryParse<ServiceModality>(entity.ServiceModality, out var mod) ? mod : BookingRequirementsFactory.ModalityFor(entity.BusinessType),
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

    private class TenantEntity
    {
        public string TenantId { get; set; } = "";
        public string BusinessName { get; set; } = "";
        public string BusinessType { get; set; } = "";
        public string? DbType { get; set; }
        public string? ConnectionString { get; set; }
        public string? TimeZoneId { get; set; }
        public string? PhoneCountryCode { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? WorkingHours { get; set; }
        public string? Services { get; set; }
        public string? AcceptedInsurance { get; set; }
        public string? Pricing { get; set; }
        public string? CustomSettings { get; set; }
        public string? Username { get; set; }
        public string? PasswordHash { get; set; }
        public string? MessageProvider { get; set; }
        public string? MessageProviderAccount { get; set; }
        public string? MessageProviderToken { get; set; }
        public string? MessageProviderPhone { get; set; }
        public string? WebhookUrl { get; set; }
        public string? BookingRequirementsJson { get; set; }
        public string? ServiceModality { get; set; }
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
