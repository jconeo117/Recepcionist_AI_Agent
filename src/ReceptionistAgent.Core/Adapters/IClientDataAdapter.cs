using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Adapters;

/// <summary>
/// Contrato único para leer/escribir datos del cliente.
/// Cada tenant/cliente implementa o configura su propio adapter
/// que conecta con su base de datos externa.
/// </summary>
public interface IClientDataAdapter
{
    // === Bookings ===
    Task<BookingRecord> CreateBookingAsync(BookingRecord booking);
    Task<BookingRecord?> GetBookingByCodeAsync(string confirmationCode);
    Task<BookingRecord?> GetBookingByIdempotencyKeyAsync(string key);
    Task<List<BookingRecord>> GetBookingsByDateAsync(DateTime date);
    Task<List<BookingRecord>> GetAllBookingsAsync();
    Task<bool> UpdateBookingAsync(BookingRecord booking);
    Task<bool> DeleteBookingAsync(string id, string deletedBy = "system");
    Task<bool> ExistsAsync(DateTime date, TimeSpan time, string providerId);
    Task<BookingStats> GetBookingStatsAsync();

    // === Client Lookups ===
    Task<BookingRecord?> GetBookingByClientIdAsync(string clientId);
    Task<List<BookingRecord>> GetBookingsByClientIdAsync(string clientId);

    // === Service Providers ===
    Task<List<ServiceProvider>> GetAllProvidersAsync();
    Task<List<ServiceProvider>> SearchProvidersAsync(string query);
    Task<bool> AddProviderAsync(ServiceProvider provider);
    Task<bool> UpdateProviderAsync(ServiceProvider provider);
    Task<bool> DeleteProviderAsync(string id);
    Task<int> GetProviderCountAsync();

    // === Outbox ===
    Task AddOutboxEventAsync(OutboxEvent @event);
    Task<List<OutboxEvent>> GetUnprocessedOutboxEventsAsync(int limit = 10);
    Task UpdateOutboxStatusAsync(Guid id, bool success, string? error = null);
}
