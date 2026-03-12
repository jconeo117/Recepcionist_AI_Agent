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
        _bookings.TryAdd(booking.Id, booking);
        return Task.FromResult(booking);
    }

    public Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode)
    {
        var booking = _bookings.Values.FirstOrDefault(b => b.ConfirmationCode == confirmationCode);
        return Task.FromResult(booking);
    }

    public Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date)
    {
        var result = _bookings.Values
            .Where(b => b.ScheduledDate.Date == date.Date)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<List<BookingRecord>> GetAllBookingsAsync()
    {
        return Task.FromResult(_bookings.Values.ToList());
    }

    public Task<bool> UpdateBookingAsync(BookingRecord booking)
    {
        if (!_bookings.ContainsKey(booking.Id))
            return Task.FromResult(false);

        booking.UpdatedAt = DateTime.UtcNow;
        _bookings[booking.Id] = booking;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteBookingAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid))
            return Task.FromResult(false);

        return Task.FromResult(_bookings.TryRemove(guid, out _));
    }

    public Task<bool> ExistsAsync(DateTime date, TimeSpan time, string providerId)
    {
        var exists = _bookings.Values.Any(b =>
            b.ScheduledDate.Date == date.Date &&
            b.ScheduledTime == time &&
            b.ProviderId == providerId &&
            b.Status != BookingStatus.Cancelled);
        return Task.FromResult(exists);
    }

    // === Client Lookups ===

    public Task<BookingRecord?> GetBookingByClientIdAsync(string clientId)
    {
        var booking = _bookings.Values.FirstOrDefault(b =>
            b.CustomFields.TryGetValue("clientId", out var pid) &&
            pid?.ToString()?.Equals(clientId, StringComparison.OrdinalIgnoreCase) == true &&
            b.Status != BookingStatus.Cancelled);
        return Task.FromResult(booking);
    }

    public Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId)
    {
        var bookings = _bookings.Values
            .Where(b =>
                b.CustomFields.TryGetValue("clientId", out var pid) &&
                pid?.ToString()?.Equals(clientId, StringComparison.OrdinalIgnoreCase) == true)
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
}
