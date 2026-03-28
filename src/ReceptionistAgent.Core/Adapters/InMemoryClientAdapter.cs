using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Utils;

namespace ReceptionistAgent.Core.Adapters;

/// <summary>
/// Implementación en memoria de IClientDataAdapter para testing y demos.
/// Recibe la lista de proveedores de servicio por constructor.
/// </summary>
public class InMemoryClientAdapter : IClientDataAdapter
{
    private readonly ConcurrentDictionary<Guid, BookingRecord> _bookings = new();
    private readonly ConcurrentDictionary<Guid, OutboxEvent> _outbox = new();
    private readonly List<ServiceProvider> _providers;

    public InMemoryClientAdapter(List<ServiceProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    // === Bookings ===

    public Task<BookingRecord> CreateBookingAsync(BookingRecord booking)
    {
        booking.Id = Guid.NewGuid();
        booking.ConfirmationCode = $"CITA-{booking.Id.ToString()[..4].ToUpper()}";
        booking.CreatedAt = DateTime.UtcNow;
        booking.IsDeleted = false;
        _bookings.TryAdd(booking.Id, booking);
        return Task.FromResult(booking);
    }

    public Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode)
    {
        var booking = _bookings.Values.FirstOrDefault(b => b.ConfirmationCode == confirmationCode && !b.IsDeleted);
        return Task.FromResult(booking);
    }

    public Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        var result = _bookings.Values
            .Where(b => b.ScheduledDate.Date == date.Date && !b.IsDeleted)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<BookingRecord?> GetBookingByIdempotencyKeyAsync(string key)
    {
        var booking = _bookings.Values.FirstOrDefault(b => b.IdempotencyKey == key && !b.IsDeleted);
        return Task.FromResult(booking);
    }

    public Task<List<BookingRecord>> GetAllBookingsAsync()
    {
        return Task.FromResult(_bookings.Values.Where(b => !b.IsDeleted).ToList());
    }

    public Task<bool> UpdateBookingAsync(BookingRecord booking)
    {
        if (!_bookings.ContainsKey(booking.Id))
            return Task.FromResult(false);

        booking.UpdatedAt = DateTime.UtcNow;
        _bookings[booking.Id] = booking;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteBookingAsync(string id, string deletedBy = "system")
    {
        if (!Guid.TryParse(id, out var guid))
            return Task.FromResult(false);

        if (_bookings.TryGetValue(guid, out var booking))
        {
            booking.IsDeleted = true;
            booking.DeletedAt = DateTime.UtcNow;
            booking.DeletedBy = deletedBy;
            booking.Status = BookingStatus.Cancelled;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> ExistsAsync(DateTime date, TimeSpan time, string providerId)
    {
        var exists = _bookings.Values.Any(b =>
            b.ScheduledDate.Date == date.Date &&
            b.ScheduledTime == time &&
            b.ProviderId == providerId &&
            b.Status != BookingStatus.Cancelled &&
            !b.IsDeleted);
        return Task.FromResult(exists);
    }

    // === Client Lookups ===

    public Task<BookingRecord?> GetBookingByClientIdAsync(string clientId)
    {
        var booking = _bookings.Values.FirstOrDefault(b =>
            b.CustomFields.TryGetValue("clientId", out var pid) &&
            pid?.ToString()?.Equals(clientId, StringComparison.OrdinalIgnoreCase) == true &&
            b.Status != BookingStatus.Cancelled &&
            !b.IsDeleted);
        return Task.FromResult(booking);
    }

    public Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId)
    {
        var bookings = _bookings.Values
            .Where(b =>
                b.CustomFields.TryGetValue("clientId", out var pid) &&
                pid?.ToString()?.Equals(clientId, StringComparison.OrdinalIgnoreCase) == true &&
                !b.IsDeleted)
            .ToList();
        return Task.FromResult(bookings);
    }

    // === Service Providers ===

    public Task<List<ServiceProvider>> GetAllProvidersAsync()
    {
        return Task.FromResult(_providers.ToList());
    }

    public Task<List<ServiceProvider>> SearchProvidersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new List<ServiceProvider>());

        var normalizedQuery = TextHelper.RemoveAccents(query.Trim());
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = _providers.Where(p =>
        {
            var normalizedName = TextHelper.RemoveAccents(p.Name);
            var normalizedRole = TextHelper.RemoveAccents(p.Role);

            return queryTokens.All(token =>
                normalizedName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                normalizedRole.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Equals(token, StringComparison.OrdinalIgnoreCase));
        }).ToList();

        return Task.FromResult(results);
    }

    public Task<bool> AddProviderAsync(ServiceProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Id))
            provider.Id = Guid.NewGuid().ToString();

        _providers.Add(provider);
        return Task.FromResult(true);
    }

    public Task<bool> UpdateProviderAsync(ServiceProvider provider)
    {
        var existing = _providers.FirstOrDefault(p => p.Id == provider.Id);
        if (existing == null) return Task.FromResult(false);

        _providers.Remove(existing);
        _providers.Add(provider);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteProviderAsync(string id)
    {
        var existing = _providers.FirstOrDefault(p => p.Id == id);
        if (existing == null) return Task.FromResult(false);

        _providers.Remove(existing);
        return Task.FromResult(true);
    }

    // === Outbox ===

    public Task AddOutboxEventAsync(OutboxEvent @event)
    {
        _outbox.TryAdd(@event.Id, @event);
        return Task.CompletedTask;
    }

    public Task<List<OutboxEvent>> GetUnprocessedOutboxEventsAsync(int limit = 10)
    {
        var events = _outbox.Values
            .Where(e => e.ProcessedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(events);
    }

    public Task UpdateOutboxStatusAsync(Guid id, bool success, string? error = null)
    {
        if (_outbox.TryGetValue(id, out var @event))
        {
            if (success)
            {
                @event.ProcessedAt = DateTime.UtcNow;
                @event.LastError = null;
            }
            else
            {
                @event.RetryCount++;
                @event.LastError = error;
            }
        }
        return Task.CompletedTask;
    }
}
