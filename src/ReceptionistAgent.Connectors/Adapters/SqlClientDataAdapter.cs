using Dapper;
using Microsoft.Data.SqlClient;
using ReceptionistAgent.Core.Models;
using System.Text.Json;
using ReceptionistAgent.Core.Adapters;

namespace ReceptionistAgent.Connectors.Adapters;

/// <summary>
/// Implementación de IClientDataAdapter usando Dapper para SQL Server.
/// </summary>
public class SqlClientDataAdapter : IClientDataAdapter
{
    private readonly string _connectionString;
    private readonly List<ServiceProvider> _tenantProviders;

    public SqlClientDataAdapter(string connectionString, List<ServiceProvider> tenantProviders)
    {
        _connectionString = connectionString;
        _tenantProviders = tenantProviders;
    }

    public Task<List<ServiceProvider>> GetAllProvidersAsync()
    {
        return Task.FromResult(_tenantProviders.ToList());
    }

    public Task<List<ServiceProvider>> SearchProvidersAsync(string query)
    {
        var result = _tenantProviders
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Role.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<BookingRecord> CreateBookingAsync(BookingRecord booking)
    {
        booking.Id = Guid.NewGuid();
        booking.ConfirmationCode = $"CITA-{booking.Id.ToString()[..4].ToUpper()}";
        booking.CreatedAt = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO Bookings (
                Id, TenantId, ConfirmationCode, ClientName, ProviderId, ProviderName, 
                ScheduledDate, ScheduledTime, Status, CreatedAt, CustomFieldsJson
            ) VALUES (
                @Id, @TenantId, @ConfirmationCode, @ClientName, @ProviderId, @ProviderName, 
                @ScheduledDate, @ScheduledTime, @Status, @CreatedAt, @CustomFieldsJson
            )";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            booking.Id,
            booking.TenantId,
            booking.ConfirmationCode,
            booking.ClientName,
            booking.ProviderId,
            booking.ProviderName,
            booking.ScheduledDate,
            booking.ScheduledTime,
            Status = booking.Status.ToString(),
            booking.CreatedAt,
            CustomFieldsJson = JsonSerializer.Serialize(booking.CustomFields)
        });

        return booking;
    }

    public async Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode)
    {
        const string sql = "SELECT * FROM Bookings WHERE ConfirmationCode = @Code";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<BookingEntity>(sql, new { Code = confirmationCode });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        const string sql = "SELECT * FROM Bookings WHERE ScheduledDate = @Date AND Status != @CancelledStatus";

        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql, new
        {
            Date = date.Date,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<List<BookingRecord>> GetAllBookingsAsync()
    {
        const string sql = "SELECT * FROM Bookings";

        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql);

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<bool> UpdateBookingAsync(BookingRecord booking)
    {
        booking.UpdatedAt = DateTime.UtcNow;

        const string sql = @"
            UPDATE Bookings SET 
                ClientName = @ClientName,
                ProviderId = @ProviderId,
                ProviderName = @ProviderName,
                ScheduledDate = @ScheduledDate,
                ScheduledTime = @ScheduledTime,
                Status = @Status,
                UpdatedAt = @UpdatedAt,
                CustomFieldsJson = @CustomFieldsJson
            WHERE Id = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new
        {
            booking.ClientName,
            booking.ProviderId,
            booking.ProviderName,
            booking.ScheduledDate,
            booking.ScheduledTime,
            Status = booking.Status.ToString(),
            booking.UpdatedAt,
            CustomFieldsJson = JsonSerializer.Serialize(booking.CustomFields),
            booking.Id
        });

        return affected > 0;
    }

    public async Task<bool> DeleteBookingAsync(string id)
    {
        if (!Guid.TryParse(id, out var bookingId)) return false;

        const string sql = "DELETE FROM Bookings WHERE Id = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new { Id = bookingId });

        return affected > 0;
    }

    public async Task<bool> ExistsAsync(DateTime date, TimeSpan time, string providerId)
    {
        const string sql = @"
            SELECT COUNT(1) FROM Bookings 
            WHERE ProviderId = @ProviderId 
              AND ScheduledDate = @Date 
              AND ScheduledTime = @Time 
              AND Status != @CancelledStatus";

        using var connection = new SqlConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>(sql, new
        {
            ProviderId = providerId,
            Date = date.Date,
            Time = time,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        return count > 0;
    }

    public async Task<BookingRecord?> GetBookingByClientIdAsync(string clientId)
    {
        const string sql = "SELECT * FROM Bookings WHERE JSON_VALUE(CustomFieldsJson, '$.clientId') = @ClientId AND Status != @CancelledStatus ORDER BY ScheduledDate DESC";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QueryFirstOrDefaultAsync<BookingEntity>(sql, new
        {
            ClientId = clientId,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId)
    {
        const string sql = "SELECT * FROM Bookings WHERE JSON_VALUE(CustomFieldsJson, '$.clientId') = @ClientId ORDER BY ScheduledDate DESC";

        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql, new { ClientId = clientId });

        return entities.Select(MapToRecord).ToList();
    }

    private static BookingRecord MapToRecord(BookingEntity entity)
    {
        return new BookingRecord
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            ConfirmationCode = entity.ConfirmationCode,
            ClientName = entity.ClientName,
            ProviderId = entity.ProviderId,
            ProviderName = entity.ProviderName,
            ScheduledDate = entity.ScheduledDate,
            ScheduledTime = entity.ScheduledTime,
            Status = Enum.TryParse<BookingStatus>(entity.Status, true, out var status) ? status : BookingStatus.Scheduled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            CustomFields = string.IsNullOrWhiteSpace(entity.CustomFieldsJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(entity.CustomFieldsJson) ?? new Dictionary<string, object>()
        };
    }
}

public class BookingEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ConfirmationCode { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public TimeSpan ScheduledTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CustomFieldsJson { get; set; } = string.Empty;
}
