using Dapper;
using Npgsql;
using ReceptionistAgent.Core.Models;
using System.Text.Json;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace ReceptionistAgent.Connectors.Adapters;

public class PostgreSqlClientDataAdapter : IClientDataAdapter
{
    private readonly string _connectionString;
    private readonly IBookingBackupService _backupService;
    private readonly ILogger<PostgreSqlClientDataAdapter> _logger;
    private bool _columnExistsChecked = false;

    public PostgreSqlClientDataAdapter(string connectionString, IBookingBackupService backupService, ILogger<PostgreSqlClientDataAdapter> logger)
    {
        _connectionString = connectionString;
        _backupService = backupService;
        _logger = logger;
    }

    private async Task EnsureIdempotencyColumnAsync()
    {
        if (_columnExistsChecked) return;

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // 1. Ensure Outbox Table (Priority)
            const string outboxSql = @"
                CREATE TABLE IF NOT EXISTS outbox_events (
                    id UUID PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload_json JSONB NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL,
                    processed_at TIMESTAMPTZ,
                    retry_count INT DEFAULT 0,
                    last_error TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_outbox_unprocessed ON outbox_events (processed_at) WHERE processed_at IS NULL;";
            await connection.ExecuteAsync(outboxSql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure outbox_events table exists.");
        }

        try
        {
            // 2. Ensure Bookings Soft Delete & Idempotency
            // We do this in separate blocks because of potential 'must be owner' errors in some environments
            const string softDeleteSql = @"
                DO $$ 
                BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='bookings' AND column_name='is_deleted') THEN
                        ALTER TABLE bookings ADD COLUMN is_deleted BOOLEAN NOT NULL DEFAULT FALSE;
                        ALTER TABLE bookings ADD COLUMN deleted_at TIMESTAMPTZ NULL;
                        ALTER TABLE bookings ADD COLUMN deleted_by TEXT NULL;
                    END IF;
                END $$;";
            await connection.ExecuteAsync(softDeleteSql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure bookings soft-delete columns (is_deleted). Check table ownership.");
        }

        try
        {
            const string idempotencySql = @"
                DO $$ 
                BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='bookings' AND column_name='idempotency_key') THEN
                        ALTER TABLE bookings ADD COLUMN idempotency_key TEXT;
                        CREATE INDEX IF NOT EXISTS idx_bookings_idempotency ON bookings(idempotency_key);
                    END IF;
                END $$;";
            await connection.ExecuteAsync(idempotencySql);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure bookings idempotency_key column. Check table ownership.");
        }

        _columnExistsChecked = true;
    }

    public async Task<List<ServiceProvider>> GetAllProvidersAsync()
    {
        const string sql = "SELECT * FROM providers WHERE is_active = true";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<ProviderEntity>(sql);

        var providers = entities.Select(MapToProvider).ToList();
        _logger.LogInformation("Loaded {Count} providers from the PostgreSQL tenant database.", providers.Count);

        return providers;
    }

    public async Task<List<ServiceProvider>> SearchProvidersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<ServiceProvider>();

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
            INSERT INTO providers (
                id, name, role, working_days, start_time, end_time, slot_duration_min, is_active
            ) VALUES (
                @Id, @Name, @Role, @WorkingDays::jsonb, @StartTime, @EndTime, @SlotDuration, @IsActive
            )";

        using var connection = new NpgsqlConnection(_connectionString);
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
            UPDATE providers SET 
                name = @Name,
                role = @Role,
                working_days = @WorkingDays::jsonb,
                start_time = @StartTime,
                end_time = @EndTime,
                slot_duration_min = @SlotDuration,
                is_active = @IsActive
            WHERE id = @Id";

        using var connection = new NpgsqlConnection(_connectionString);
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
        // En PostgreSQL usualmente solo desactivamos para no romper el historial de reservas
        // o borramos físicamente si se prefiere. Se usará eliminación física.
        const string sql = "DELETE FROM providers WHERE id = @Id";

        using var connection = new NpgsqlConnection(_connectionString);
        var affected = await connection.ExecuteAsync(sql, new { Id = id });

        return affected > 0;
    }

    public async Task<int> GetProviderCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM providers WHERE is_active = true";
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    public async Task<BookingStats> GetBookingStatsAsync()
    {
        const string sql = @"
            SELECT 
                COUNT(*) as TotalBookings,
                COUNT(*) FILTER (WHERE status = 'Scheduled') as ScheduledCount,
                COUNT(*) FILTER (WHERE status = 'Confirmed') as ConfirmedCount,
                COUNT(*) FILTER (WHERE status = 'Cancelled') as CancelledCount,
                COUNT(*) FILTER (WHERE status = 'Completed') as CompletedCount,
                COUNT(*) FILTER (WHERE status = 'NoShow') as NoShowCount,
                COUNT(*) FILTER (WHERE status = 'EscalatedToHuman') as EscalatedCount
            FROM bookings 
            WHERE is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QuerySingleAsync<BookingStats>(sql);
    }

    public async Task<BookingRecord> CreateBookingAsync(BookingRecord booking)
    {
        await EnsureIdempotencyColumnAsync();
        booking.Id = Guid.NewGuid();
        booking.ConfirmationCode = $"CITA-{booking.Id.ToString()[..4].ToUpper()}";
        booking.CreatedAt = DateTime.UtcNow;

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Validar disponibilidad de forma atómica con bloqueo de fila
            const string checkSql = @"
                SELECT COUNT(1) FROM bookings 
                WHERE provider_id = @ProviderId 
                  AND scheduled_date = @Date 
                  AND scheduled_time = @Time 
                  AND status != 'Cancelled'
                  AND is_deleted = false
                FOR UPDATE";

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
                INSERT INTO bookings (
                    id, tenant_id, confirmation_code, client_name, provider_id, provider_name, 
                    scheduled_date, scheduled_time, status, created_at, custom_fields_json, idempotency_key
                ) VALUES (
                    @Id, @TenantId, @ConfirmationCode, @ClientName, @ProviderId, @ProviderName, 
                    @ScheduledDate, @ScheduledTime, @Status, @CreatedAt, @CustomFieldsJson::jsonb, @IdempotencyKey
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
                ScheduledTime = booking.ScheduledTime,
                Status = booking.Status.ToString(),
                booking.CreatedAt,
                CustomFieldsJson = JsonSerializer.Serialize(booking.CustomFields),
                booking.IdempotencyKey
            }, transaction);

            await _backupService.BackupAsync(booking, booking.TenantId);
            await transaction.CommitAsync();
            return booking;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transaccional creando booking en PostgreSQL.");
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode)
    {
        const string sql = "SELECT * FROM bookings WHERE confirmation_code = @Code AND is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<BookingEntity>(sql, new { Code = confirmationCode });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<BookingRecord?> GetBookingByIdempotencyKeyAsync(string key)
    {
        const string sql = "SELECT * FROM bookings WHERE idempotency_key = @Key AND is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QuerySingleOrDefaultAsync<BookingEntity>(sql, new { Key = key });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        const string sql = "SELECT * FROM bookings WHERE scheduled_date = @Date AND status != @CancelledStatus AND is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql, new
        {
            Date = date.Date,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<List<BookingRecord>> GetAllBookingsAsync()
    {
        const string sql = "SELECT * FROM bookings WHERE is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql);

        return entities.Select(MapToRecord).ToList();
    }

    public async Task<bool> UpdateBookingAsync(BookingRecord booking)
    {
        booking.UpdatedAt = DateTime.UtcNow;

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Validar concurrencia con bloqueo FOR UPDATE
            const string checkSql = @"
                SELECT COUNT(1) FROM bookings 
                WHERE provider_id = @ProviderId 
                  AND scheduled_date = @Date 
                  AND scheduled_time = @Time 
                  AND status != 'Cancelled'
                  AND is_deleted = false
                  AND id != @Id
                FOR UPDATE";

            var exists = await connection.ExecuteScalarAsync<int>(checkSql, new
            {
                ProviderId = booking.ProviderId,
                Date = booking.ScheduledDate.Date,
                Time = booking.ScheduledTime,
                Id = booking.Id
            }, transaction) > 0;

            if (exists)
            {
                _logger.LogWarning("Conflicto detectado durante update de booking {Id} en Postgres. Slot ocupado.", booking.Id);
                await transaction.RollbackAsync();
                return false;
            }

            const string sql = @"
                UPDATE bookings SET 
                    client_name = @ClientName,
                    provider_id = @ProviderId,
                    provider_name = @ProviderName,
                    scheduled_date = @ScheduledDate,
                    scheduled_time = @ScheduledTime,
                    status = @Status,
                    updated_at = @UpdatedAt,
                    custom_fields_json = @CustomFieldsJson::jsonb
                WHERE id = @Id";

            var affected = await connection.ExecuteAsync(sql, new
            {
                booking.ClientName,
                booking.ProviderId,
                booking.ProviderName,
                booking.ScheduledDate,
                ScheduledTime = booking.ScheduledTime,
                Status = booking.Status.ToString(),
                booking.UpdatedAt,
                CustomFieldsJson = JsonSerializer.Serialize(booking.CustomFields),
                booking.Id
            }, transaction);

            if (affected > 0)
            {
                await _backupService.UpdateStatusBackupAsync(booking.Id, booking.TenantId, booking.Status);
                await transaction.CommitAsync();
                return true;
            }

            await transaction.RollbackAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transaccional actualizando booking {Id} en PostgreSQL.", booking.Id);
            await transaction.RollbackAsync();
            return false;
        }
    }

    public async Task<bool> DeleteBookingAsync(string id, string deletedBy = "system")
    {
        if (!Guid.TryParse(id, out var bookingId)) return false;

        const string sql = @"
            UPDATE bookings SET 
                is_deleted = true, 
                deleted_at = @Now, 
                deleted_by = @DeletedBy,
                status = @CancelledStatus
            WHERE id = @Id";

        using var connection = new NpgsqlConnection(_connectionString);
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
            SELECT COUNT(1) FROM bookings 
            WHERE provider_id = @ProviderId 
              AND scheduled_date = @Date 
              AND scheduled_time = @Time 
              AND status != @CancelledStatus
              AND is_deleted = false";

        using var connection = new NpgsqlConnection(_connectionString);
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
        const string sql = "SELECT * FROM bookings WHERE custom_fields_json ->> 'clientId' = @ClientId AND status != @CancelledStatus AND is_deleted = false ORDER BY scheduled_date DESC LIMIT 1";

        using var connection = new NpgsqlConnection(_connectionString);
        var entity = await connection.QueryFirstOrDefaultAsync<BookingEntity>(sql, new
        {
            ClientId = clientId,
            CancelledStatus = BookingStatus.Cancelled.ToString()
        });

        return entity == null ? null : MapToRecord(entity);
    }

    public async Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId)
    {
        const string sql = "SELECT * FROM bookings WHERE custom_fields_json ->> 'clientId' = @ClientId AND is_deleted = false ORDER BY scheduled_date DESC";

        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<BookingEntity>(sql, new { ClientId = clientId });

        return entities.Select(MapToRecord).ToList();
    }

    private static BookingRecord MapToRecord(BookingEntity entity)
    {
        return new BookingRecord
        {
            Id = entity.id,
            TenantId = entity.tenant_id,
            ConfirmationCode = entity.confirmation_code,
            ClientName = entity.client_name,
            ProviderId = entity.provider_id,
            ProviderName = entity.provider_name,
            ScheduledDate = entity.scheduled_date.ToDateTime(TimeOnly.MinValue),
            ScheduledTime = entity.scheduled_time.ToTimeSpan(),
            Status = Enum.TryParse<BookingStatus>(entity.status, true, out var status) ? status : BookingStatus.Scheduled,
            CreatedAt = entity.created_at,
            UpdatedAt = entity.updated_at,
            IsDeleted = entity.is_deleted,
            DeletedAt = entity.deleted_at,
            DeletedBy = entity.deleted_by,
            IdempotencyKey = entity.idempotency_key,
            CustomFields = string.IsNullOrWhiteSpace(entity.custom_fields_json)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(entity.custom_fields_json) ?? new Dictionary<string, object>()
        };
    }

    // === Outbox Implementation ===
    public async Task AddOutboxEventAsync(OutboxEvent @event)
    {
        await EnsureIdempotencyColumnAsync(); // Reuses the schema check
        const string sql = @"
            INSERT INTO outbox_events (id, tenant_id, event_type, payload_json, created_at)
            VALUES (@Id, @TenantId, @EventType, @PayloadJson::jsonb, @CreatedAt)";
        
        using var connection = new NpgsqlConnection(_connectionString);
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
        const string sql = "SELECT * FROM outbox_events WHERE processed_at IS NULL ORDER BY created_at LIMIT @Limit";
        using var connection = new NpgsqlConnection(_connectionString);
        var entities = await connection.QueryAsync<OutboxEntity>(sql, new { Limit = limit });
        return entities.Select(e => new OutboxEvent
        {
            Id = e.id,
            TenantId = e.tenant_id,
            EventType = e.event_type,
            PayloadJson = e.payload_json,
            CreatedAt = e.created_at,
            ProcessedAt = e.processed_at,
            RetryCount = e.retry_count,
            LastError = e.last_error
        }).ToList();
    }

    public async Task UpdateOutboxStatusAsync(Guid id, bool success, string? error = null)
    {
        string sql = success 
            ? "UPDATE outbox_events SET processed_at = @Now, last_error = NULL WHERE id = @Id"
            : "UPDATE outbox_events SET retry_count = retry_count + 1, last_error = @Error WHERE id = @Id";
        
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new { Id = id, Now = DateTime.UtcNow, Error = error });
    }

    private class OutboxEntity
    {
        public Guid id { get; set; }
        public string tenant_id { get; set; } = "";
        public string event_type { get; set; } = "";
        public string payload_json { get; set; } = "";
        public DateTime created_at { get; set; }
        public DateTime? processed_at { get; set; }
        public int retry_count { get; set; }
        public string? last_error { get; set; }
    }

    private static ServiceProvider MapToProvider(ProviderEntity entity)
    {
        return new ServiceProvider
        {
            Id = entity.id,
            Name = entity.name,
            Role = entity.role,
            WorkingDays = ParseWorkingDays(entity.working_days),
            StartTime = entity.start_time.ToTimeSpan(),
            EndTime = entity.end_time.ToTimeSpan(),
            SlotDurationMinutes = entity.slot_duration_min,
            IsAvailable = entity.is_active
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

    private class BookingEntity
    {
        public Guid id { get; set; }
        public string tenant_id { get; set; } = string.Empty;
        public string confirmation_code { get; set; } = string.Empty;
        public string client_name { get; set; } = string.Empty;
        public string provider_id { get; set; } = string.Empty;
        public string provider_name { get; set; } = string.Empty;
        public DateOnly scheduled_date { get; set; }
        public TimeOnly scheduled_time { get; set; }
        public string status { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime? updated_at { get; set; }
        public bool is_deleted { get; set; }
        public DateTime? deleted_at { get; set; }
        public string? deleted_by { get; set; }
        public string custom_fields_json { get; set; } = string.Empty;
        public string? idempotency_key { get; set; }
    }

    private class ProviderEntity
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string role { get; set; } = string.Empty;
        public string? working_days { get; set; }
        public TimeOnly start_time { get; set; }
        public TimeOnly end_time { get; set; }
        public int slot_duration_min { get; set; }
        public bool is_active { get; set; }
    }
}
