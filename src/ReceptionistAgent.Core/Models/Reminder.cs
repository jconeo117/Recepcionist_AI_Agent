namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Recordatorio programado para una cita.
/// Se crean automáticamente al agendar un booking (24h y 1-2h antes).
/// </summary>
public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public Guid BookingId { get; set; }
    public ReminderType ReminderType { get; set; }

    /// <summary>
    /// Fecha/hora en que se debe enviar el recordatorio (UTC).
    /// </summary>
    public DateTime ScheduledFor { get; set; }

    public ReminderStatus Status { get; set; } = ReminderStatus.Pending;
    public string Channel { get; set; } = "WhatsApp";
    public string RecipientPhone { get; set; } = string.Empty;
    public string? MessageContent { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ReminderType
{
    Before24h = 0,
    Before1h = 1,
    Confirmation = 2    // Futuro
}

public enum ReminderStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Cancelled = 3
}
