using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Services;

/// <summary>
/// Servicio de recordatorios de citas.
/// Agenda recordatorios automáticos a 24h y 1-2h antes.
/// </summary>
public interface IReminderService
{
    /// <summary>
    /// Agenda recordatorios a 24h y 1-2h antes de la cita.
    /// </summary>
    Task ScheduleRemindersForBookingAsync(BookingRecord booking, string recipientPhone, string PhoneCountryCode = "");

    /// <summary>
    /// Obtiene recordatorios pendientes cuya hora de envío ya pasó.
    /// </summary>
    Task<List<Reminder>> GetPendingRemindersAsync(DateTime before);

    Task MarkAsSentAsync(Guid reminderId);
    Task MarkAsFailedAsync(Guid reminderId, string error);

    /// <summary>
    /// Cancela recordatorios pendientes al cancelar una cita.
    /// </summary>
    Task CancelRemindersForBookingAsync(Guid bookingId);
}
