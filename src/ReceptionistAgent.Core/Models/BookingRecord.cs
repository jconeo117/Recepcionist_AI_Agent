namespace ReceptionistAgent.Core.Models;

public class BookingRecord
{
    // Identificación
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ConfirmationCode { get; set; } = string.Empty;

    // Cliente (quien solicita la cita)
    public string ClientName { get; set; } = string.Empty;

    // Proveedor de servicio (doctor, estilista, mecánico, etc.)
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    // Fecha y hora
    public DateTime ScheduledDate { get; set; }
    public TimeSpan ScheduledTime { get; set; }

    // Estado
    public BookingStatus Status { get; set; } = BookingStatus.Scheduled;

    // Campos dinámicos definidos por cada tenant
    public Dictionary<string, object> CustomFields { get; set; } = new();

    // Metadata
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public enum BookingStatus
{
    Scheduled = 0,    // Agendada
    Confirmed = 1,    // Confirmada
    Cancelled = 2,    // Cancelada
    Completed = 3,    // Completada
    NoShow = 4,       // No se presentó
    EscalatedToHuman = 5  // Escalada a personal humano (futuro)
}
