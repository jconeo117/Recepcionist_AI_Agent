using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Security;
using System.Text.Json;

namespace ReceptionistAgent.Core.Services;

public class BookingService : IBookingService
{
    private readonly IClientDataAdapter _adapter;
    private readonly TenantContext _tenantContext;
    private readonly IAuditLogger _auditLogger;

    public BookingService(IClientDataAdapter adapter, TenantContext tenantContext, IAuditLogger auditLogger)
    {
        _adapter = adapter;
        _tenantContext = tenantContext;
        _auditLogger = auditLogger;
    }

    public async Task<List<TimeSlot>> GetAvailableSlotsAsync(string providerId, DateTime date)
    {
        var providers = await _adapter.GetAllProvidersAsync();
        var provider = providers.FirstOrDefault(p => p.Id == providerId)
            ?? throw new ArgumentException("Proveedor no encontrado");

        if (!provider.WorkingDays.Contains(date.DayOfWeek))
            return [];

        var slots = new List<TimeSlot>();
        var currentTime = provider.StartTime;
        var slotDuration = TimeSpan.FromMinutes(provider.SlotDurationMinutes);

        while (currentTime < provider.EndTime)
        {
            var slot = new TimeSlot
            {
                Date = date,
                Time = currentTime,
                IsAvailable = !await _adapter.ExistsAsync(date, currentTime, providerId)
            };

            slots.Add(slot);
            currentTime = currentTime.Add(slotDuration);
        }

        return slots;
    }

    public async Task<BookingRecord> CreateBookingAsync(
        string clientName,
        string providerId,
        DateTime date,
        TimeSpan time,
        Dictionary<string, object>? customFields = null,
        string? idempotencyKey = null)
    {
        if (await _adapter.ExistsAsync(date, time, providerId))
        {
            throw new InvalidOperationException("El horario ya está ocupado");
        }

        var providers = await _adapter.GetAllProvidersAsync();
        var provider = providers.FirstOrDefault(p => p.Id == providerId)
            ?? throw new Exception("Proveedor no encontrado");

        if (!provider.WorkingDays.Contains(date.DayOfWeek))
        {
            throw new InvalidOperationException("El proveedor no trabaja en esa fecha");
        }

        if (time < provider.StartTime || time > provider.EndTime)
        {
            throw new InvalidOperationException("El horario está fuera del rango del proveedor");
        }

        var booking = new BookingRecord
        {
            TenantId = _tenantContext.CurrentTenant?.TenantId ?? string.Empty,
            ClientName = clientName,
            ProviderId = providerId,
            ProviderName = provider.Name,
            ScheduledDate = date,
            ScheduledTime = time,
            Status = BookingStatus.Confirmed,
            IdempotencyKey = idempotencyKey,
            CustomFields = customFields ?? new Dictionary<string, object>()
        };

        var created = await _adapter.CreateBookingAsync(booking);

        // --- OUTBOX PATTERN ---
        await _adapter.AddOutboxEventAsync(new OutboxEvent
        {
            TenantId = _tenantContext.CurrentTenant?.TenantId ?? "unknown",
            EventType = "BookingCreated",
            PayloadJson = JsonSerializer.Serialize(created),
            CreatedAt = DateTime.UtcNow
        });

        return created;
    }

    public async Task<bool> CancelBookingAsync(string confirmationCode)
    {
        var booking = await _adapter.GetBookingByCodeAsync(confirmationCode);
        if (booking == null) return false;
        booking.Status = BookingStatus.Cancelled;
        var success = await _adapter.UpdateBookingAsync(booking);

        if (success)
        {
            await _auditLogger.LogAsync(new AuditEntry
            {
                TenantId = _tenantContext.CurrentTenant?.TenantId ?? "unknown",
                EventType = "BookingCancelled",
                Content = $"Reserva {confirmationCode} cancelada.",
                Metadata = new Dictionary<string, string> { { "confirmationCode", confirmationCode } }
            });

            await _adapter.AddOutboxEventAsync(new OutboxEvent
            {
                TenantId = _tenantContext.CurrentTenant?.TenantId ?? "unknown",
                EventType = "BookingCancelled",
                PayloadJson = JsonSerializer.Serialize(booking),
                CreatedAt = DateTime.UtcNow
            });
        }

        return success;
    }

    public async Task<bool> DeleteBookingAsync(string id, string deletedBy = "system")
    {
        var success = await _adapter.DeleteBookingAsync(id, deletedBy);
        if (success)
        {
            await _auditLogger.LogAsync(new AuditEntry
            {
                TenantId = _tenantContext.CurrentTenant?.TenantId ?? "unknown",
                EventType = "BookingDeleted",
                Content = $"Reserva {id} eliminada (soft-delete) por {deletedBy}.",
                Metadata = new Dictionary<string, string> 
                { 
                    { "bookingId", id },
                    { "deletedBy", deletedBy }
                }
            });
        }
        return success;
    }

    public async Task<BookingRecord?> GetBookingAsync(string confirmationCode)
    {
        return await _adapter.GetBookingByCodeAsync(confirmationCode);
    }

    public async Task<BookingRecord?> GetBookingByIdempotencyKeyAsync(string key)
    {
        return await _adapter.GetBookingByIdempotencyKeyAsync(key);
    }

    public async Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        return await _adapter.GetBookingsByDateAsync(date);
    }

    public async Task<BookingRecord?> GetBookingByClientIdAsync(string clientId)
    {
        return await _adapter.GetBookingByClientIdAsync(clientId);
    }

    public async Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId)
    {
        return await _adapter.GetBookingsByClientIdAsync(clientId);
    }

    public async Task<List<ServiceProvider>> GetAllProvidersAsync()
    {
        return await _adapter.GetAllProvidersAsync();
    }

    public async Task<List<ServiceProvider>> SearchProvidersAsync(string query)
    {
        return await _adapter.SearchProvidersAsync(query);
    }
}
