using Dapper;
using Microsoft.Data.SqlClient;
using ReceptionistAgent.Core.Models;
using System.Text.Json;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace ReceptionistAgent.Connectors.Adapters;

public class SqlClientDataAdapter : IClientDataAdapter
{
    private readonly string _connectionString;
    private readonly IBookingBackupService _backupService;
    private readonly ILogger<SqlClientDataAdapter> _logger;
    private bool _columnExistsChecked = false;

    public SqlClientDataAdapter(string connectionString, IBookingBackupService backupService, ILogger<SqlClientDataAdapter> logger)
    {
        _connectionString = connectionString;
        _backupService = backupService;
        _logger = logger;
    }

    private async Task EnsureIdempotencyColumnAsync()
    {
        if (_columnExistsChecked) return;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // 1. Ensure Outbox Table
            const string outboxSql = @"
                IF OBJECT_ID('OutboxEvents') IS NULL
                BEGIN
                    CREATE TABLE OutboxEvents (
                        Id UNIQUEIDENTIFIER PRIMARY KEY,
                        TenantId NVARCHAR(100) NOT NULL,
                        EventType NVARCHAR(100) NOT NULL,
                        PayloadJson NVARCHAR(MAX) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL,
                        ProcessedAt DATETIME2 NULL,
                        RetryCount INT DEFAULT 0,
                        LastError NVARCHAR(MAX) NULL
                    );
                    CREATE INDEX IX_OutboxEvents_Unprocessed ON OutboxEvents (ProcessedAt) WHERE ProcessedAt IS NULL;
                END";
            await connection.ExecuteAsync(outboxSql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure OutboxEvents table exists.");
        }

        try
        {
            // 2. Ensure Bookings Soft Delete Columns
            const string softDeleteSql = @"
                IF NOT EXISTS (SELECT 1 FROM sys.columns 
                               WHERE object_id = OBJECT_ID('Bookings') 
                                 AND name = 'IsDeleted')
                BEGIN
                    ALTER TABLE Bookings ADD IsDeleted BIT NOT NULL DEFAULT 0;
                    ALTER TABLE Bookings ADD DeletedAt DATETIME2 NULL;
                    ALTER TABLE Bookings ADD DeletedBy NVARCHAR(100) NULL;
                END";
            await connection.ExecuteAsync(softDeleteSql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure Bookings soft-delete columns.");
        }

        try
        {
            // 3. Ensure Bookings Idempotency
            const string idempotencySql = @"
                IF NOT EXISTS (SELECT 1 FROM sys.columns 
                               WHERE object_id = OBJECT_ID('Bookings') 
                                 AND name = 'IdempotencyKey')
                BEGIN
                    ALTER TABLE Bookings ADD IdempotencyKey NVARCHAR(255) NULL;
                    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bookings_Idempotency')
                    BEGIN
                        CREATE INDEX IX_Bookings_Idempotency ON Bookings(IdempotencyKey);
                    END
                END";
            await connection.ExecuteAsync(idempotencySql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure Bookings idempotency_key column.");
        }

        _columnExistsChecked = true;
    }

    public async Task<List<ServiceProvider>> GetAllProvidersAsync()
    {
        const string sql = "SELECT * FROM Providers WHERE IsActive = 1";

        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<ProviderEntity>(sql);

        var providers = entities.Select(MapToProvider).ToList();
        _logger.LogInformation("Loaded {Count} providers from the tenant database.", providers.Count);

        return providers;
    }

    public async Task<List<ServiceProvider>> SearchProvidersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<ServiceProvider>();

        // For simplicity and to reuse the logic of RemoveAccents, 
        // we fetch all and filter in memory as the number of providers is small (5-20).
        var providers = await GetAllProvidersAsync();
        var normalizedQuery = ReceptionistAgent.Core.Utils.TextHelper.RemoveAccents(query);

        return providers
            .Where(p => 
                ReceptionistAgent.Core.Utils.TextHelper.RemoveAccents(p.Name).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                ReceptionistAgent.Core.Utils.TextHelper.RemoveAccents(p.Role).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<bool> AddProviderAsync(ServiceProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Id))
            provider.Id = Guid.NewGuid().ToString();

        const string sql = @"
            INSERT INTO Providers (
                Id, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive
            ) VALUES (
                @Id, @Name, @Role, @WorkingDays, @StartTime, @EndTime, @SlotDuration, @IsActive
            )";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new
        {
            provider.Id,
            provider.Name,
            provider.Role,
            WorkingDays = JsonSerializer.Serialize(provider.WorkingDays),
            StartTime = provider.StartTime,
            EndTime = provider.EndTime,
            SlotDuration = provider.SlotDurationMinutes,
            IsActive = provider.IsAvailable
        });

        return affected > 0;
    }

    public async Task<bool> UpdateProviderAsync(ServiceProvider provider)
    {
        const string sql = @"
            UPDATE Providers SET 
                Name = @Name,
                Role = @Role,
                WorkingDays = @WorkingDays,
                StartTime = @StartTime,
                EndTime = @EndTime,
                SlotDurationMin = @SlotDuration,
                IsActive = @IsActive
            WHERE Id = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new
        {
            provider.Id,
            provider.Name,
            provider.Role,
            WorkingDays = JsonSerializer.Serialize(provider.WorkingDays),
            StartTime = provider.StartTime,
            EndTime = provider.EndTime,
            SlotDuration = provider.SlotDurationMinutes,
            IsActive = provider.IsAvailable
        });

        return affected > 0;
    }

    public async Task<bool> DeleteProviderAsync(string id)
    {
        const string sql = "DELETE FROM Providers WHERE Id = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new { Id = id });

        return affected > 0;
    }

    public async Task<int> GetProviderCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM Providers WHERE IsActive = 1";
        using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    public async Task<BookingStats> GetBookingStatsAsync()
    {
        const string sql = @"
            SELECT 
                COUNT(*) as TotalBookings,
                COUNT(CASE WHEN Status = 'Scheduled' THEN 1 END) as ScheduledCount,
                COUNT(CASE WHEN Status = 'Confirmed' THEN 1 END) as ConfirmedCount,
                COUNT(CASE WHEN Status = 'Cancelled' THEN 1 END) as CancelledCount,
                COUNT(CASE WHEN Status = 'Completed' THEN 1 END) as CompletedCount,
                COUNT(CASE WHEN Status = 'NoShow' THEN 1 END) as NoShowCount,
                COUNT(CASE WHEN Status = 'EscalatedToHuman' THEN 1 END) as EscalatedCount
            FROM Bookings 
            WHERE IsDeleted = 0";

        using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleAsync<BookingStats>(sql);
    }

    public async Task<BookingRecord> CreateBookingAsync(BookingRecord booking)
    {
        await EnsureIdempotencyColumnAsync();
        booking.Id = Guid.NewGuid();
        booking.ConfirmationCode = $"CITA-{booking.Id.ToString()[..4].ToUpper()}";
        booking.CreatedAt = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Validar disponibilidad de forma atómica con bloqueo de lectura
            const string checkSql = @"
                SELECT COUNT(1) FROM Bookings WITH (UPDLOCK)
                WHERE ProviderId = @ProviderId 
                  AND ScheduledDate = @Date 
                  AND ScheduledTime = @Time 
                  AND Status != 'Cancelled'
                  AND IsDeleted = 0";

            var exists = await connection.ExecuteScalarAsync<int>(checkSql, new
            {
                ProviderId = booking.ProviderId,
                Date = booking.ScheduledDate.Date,
                Time = booking.ScheduledTime
            }, transaction) > 0;

            if (exists)
            {
                throw new InvalidOperationException("El horario seleccionado ya no está disponible.");
            }

            const string sql = @"
                INSERT INTO Bookings (
                    Id, TenantId, ConfirmationCode, ClientName, ProviderId, ProviderName, 
                    ScheduledDate, ScheduledTime, Status, CreatedAt, CustomFieldsJson, IdempotencyKey
                ) VALUES (
                    @Id, @TenantId, @ConfirmationCode, @ClientName, @ProviderId, @ProviderName, 
                    @ScheduledDate, @ScheduledTime, @Status, @CreatedAt, @CustomFieldsJson, @IdempotencyKey
                )";

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
                CustomFieldsJson = JsonSerializer.Serialize(booking.CustomFields),
                booking.IdempotencyKey
            }, transaction);

            await _backupService.BackupAsync(booking, booking.TenantId);
            transaction.Commit();
            return booking;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transaccional creando booking en SQL Server.");
            transaction.Rollback();
            throw;
        }
    }

    public async Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode)
    {
        const string sql = "SELECT * FROM Bookings WHERE ConfirmationCode = @Code AND IsDeleted = 0";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<BookingEntity>(sql, new { Code = confirmationCode });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<BookingRecord?> GetBookingByIdempotencyKeyAsync(string key)
    {
        const string sql = "SELECT * FROM Bookings WHERE IdempotencyKey = @Key AND IsDeleted = 0";

        using var connection = new SqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<BookingEntity>(sql, new { Key = key });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        const string sql = "SELECT * FROM Bookings WHERE ScheduledDate = @Date AND Status != @CancelledStatus AND IsDeleted = 0";

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
        const string sql = "SELECT * FROM Bookings WHERE IsDeleted = 0";

        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql);

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<bool> UpdateBookingAsync(BookingRecord booking)
    {
        booking.UpdatedAt = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Validar concurrencia: que no se haya ocupado el slot por otro mientras se editaba
            const string checkSql = @"
                SELECT COUNT(1) FROM Bookings WITH (UPDLOCK)
                WHERE ProviderId = @ProviderId 
                  AND ScheduledDate = @Date 
                  AND ScheduledTime = @Time 
                  AND Status != 'Cancelled'
                  AND IsDeleted = 0
                  AND Id != @Id";

            var exists = await connection.ExecuteScalarAsync<int>(checkSql, new
            {
                ProviderId = booking.ProviderId,
                Date = booking.ScheduledDate.Date,
                Time = booking.ScheduledTime,
                Id = booking.Id
            }, transaction) > 0;

            if (exists)
            {
                _logger.LogWarning("Conflicto detected durante update de booking {Id}. Slot ocupado.", booking.Id);
                transaction.Rollback();
                return false;
            }

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
            }, transaction);

            if (affected > 0)
            {
                await _backupService.UpdateStatusBackupAsync(booking.Id, booking.TenantId, booking.Status);
                transaction.Commit();
                return true;
            }
            
            transaction.Rollback();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transaccional actualizando booking {Id} en SQL Server.", booking.Id);
            transaction.Rollback();
            return false;
        }
    }

    public async Task<bool> DeleteBookingAsync(string id, string deletedBy = "system")
    {
        if (!Guid.TryParse(id, out var bookingId)) return false;

        const string sql = @"
            UPDATE Bookings SET 
                IsDeleted = 1, 
                DeletedAt = @Now, 
                DeletedBy = @DeletedBy,
                Status = @CancelledStatus
            WHERE Id = @Id";

        using var connection = new SqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new 
        { 
            Id = bookingId, 
            Now = DateTime.UtcNow, 
            DeletedBy = deletedBy,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        if (affected > 0)
        {
            await _backupService.UpdateStatusBackupAsync(bookingId, "central", BookingStatus.Cancelled);
        }

        return affected > 0;
    }

    public async Task<bool> ExistsAsync(DateTime date, TimeSpan time, string providerId)
    {
        const string sql = @"
            SELECT COUNT(1) FROM Bookings 
            WHERE ProviderId = @ProviderId 
              AND ScheduledDate = @Date 
              AND ScheduledTime = @Time 
              AND Status != @CancelledStatus
              AND IsDeleted = 0";

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
        const string sql = "SELECT * FROM Bookings WHERE JSON_VALUE(CustomFieldsJson, '$.clientId') = @ClientId AND Status != @CancelledStatus AND IsDeleted = 0 ORDER BY ScheduledDate DESC";

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
        const string sql = "SELECT * FROM Bookings WHERE JSON_VALUE(CustomFieldsJson, '$.clientId') = @ClientId AND IsDeleted = 0 ORDER BY ScheduledDate DESC";

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
            IsDeleted = entity.IsDeleted,
            DeletedAt = entity.DeletedAt,
            DeletedBy = entity.DeletedBy,
            IdempotencyKey = entity.IdempotencyKey,
            CustomFields = string.IsNullOrWhiteSpace(entity.CustomFieldsJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(entity.CustomFieldsJson) ?? new Dictionary<string, object>()
        };
    }

    // === Outbox Implementation ===
    public async Task AddOutboxEventAsync(OutboxEvent @event)
    {
        await EnsureIdempotencyColumnAsync();
        const string sql = @"
            INSERT INTO OutboxEvents (Id, TenantId, EventType, PayloadJson, CreatedAt)
            VALUES (@Id, @TenantId, @EventType, @PayloadJson, @CreatedAt)";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new 
        {
            @event.Id,
            @event.TenantId,
            @event.EventType,
            @event.PayloadJson,
            @event.CreatedAt
        });
    }

    public async Task<List<OutboxEvent>> GetUnprocessedOutboxEventsAsync(int limit = 10)
    {
        await EnsureIdempotencyColumnAsync();
        const string sql = "SELECT TOP (@Limit) * FROM OutboxEvents WHERE ProcessedAt IS NULL ORDER BY CreatedAt";
        using var connection = new SqlConnection(_connectionString);
        var entities = await connection.QueryAsync<OutboxEntity>(sql, new { Limit = limit });
        return entities.Select(e => new OutboxEvent
        {
            Id = e.Id,
            TenantId = e.TenantId,
            EventType = e.EventType,
            PayloadJson = e.PayloadJson,
            CreatedAt = e.CreatedAt,
            ProcessedAt = e.ProcessedAt,
            RetryCount = e.RetryCount,
            LastError = e.LastError
        }).ToList();
    }

    public async Task UpdateOutboxStatusAsync(Guid id, bool success, string? error = null)
    {
        string sql = success 
            ? "UPDATE OutboxEvents SET ProcessedAt = @Now, LastError = NULL WHERE Id = @Id"
            : "UPDATE OutboxEvents SET RetryCount = RetryCount + 1, LastError = @Error WHERE Id = @Id";
        
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id, Now = DateTime.UtcNow, Error = error });
    }

    private class OutboxEntity
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = "";
        public string EventType { get; set; } = "";
        public string PayloadJson { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }

    private static ServiceProvider MapToProvider(ProviderEntity entity)
    {
        return new ServiceProvider
        {
            Id = entity.Id,
            Name = entity.Name,
            Role = entity.Role,
            WorkingDays = ParseWorkingDays(entity.WorkingDays),
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            SlotDurationMinutes = entity.SlotDurationMin,
            IsAvailable = entity.IsActive
        };
    }

    private static List<DayOfWeek> ParseWorkingDays(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<DayOfWeek>();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return JsonSerializer.Deserialize<List<DayOfWeek>>(json, options) ?? new List<DayOfWeek>();
        }
        catch
        {
            // Fallback: try to deserialize as string list and parse manually
            try
            {
                var strings = JsonSerializer.Deserialize<List<string>>(json);
                if (strings == null) return new List<DayOfWeek>();
                return strings
                    .Select(s => Enum.TryParse<DayOfWeek>(s, true, out var day) ? day : (DayOfWeek?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToList();
            }
            catch { return new List<DayOfWeek>(); }
        }
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
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public string CustomFieldsJson { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
}

public class ProviderEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? WorkingDays { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int SlotDurationMin { get; set; }
    public bool IsActive { get; set; }
}
