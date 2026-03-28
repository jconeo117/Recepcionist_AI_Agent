namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Contenedor ligero para estadísticas de reservas sin cargar registros completos.
/// </summary>
public class BookingStats
{
    public int TotalBookings { get; set; }
    public int ScheduledCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int CancelledCount { get; set; }
    public int CompletedCount { get; set; }
    public int NoShowCount { get; set; }
    public int EscalatedCount { get; set; }
}
