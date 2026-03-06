namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Modelo de escalación a personal humano.
/// Solo estructura — la implementación completa es futura.
/// </summary>
public class Escalation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public EscalationStatus Status { get; set; } = EscalationStatus.Pending;
    public string? Reason { get; set; }
    public string? AssignedTo { get; set; }
    public string? ClientPhone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? Notes { get; set; }
}

public enum EscalationStatus
{
    Pending = 0,
    InProgress = 1,
    Resolved = 2
}
