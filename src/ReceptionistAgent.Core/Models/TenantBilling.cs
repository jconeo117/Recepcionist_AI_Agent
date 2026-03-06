namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Control de acceso y facturación de un tenant.
/// La lógica detallada de cobros se implementará después.
/// </summary>
public class TenantBilling
{
    public string TenantId { get; set; } = string.Empty;
    public PlanType PlanType { get; set; } = PlanType.Trial;
    public BillingStatus BillingStatus { get; set; } = BillingStatus.Active;

    /// <summary>
    /// Fecha hasta la cual el tenant tiene acceso al agente.
    /// null = sin fecha de expiración (acceso ilimitado).
    /// </summary>
    public DateTime? ActiveUntil { get; set; }

    public DateTime? SuspendedAt { get; set; }
    public string? SuspensionReason { get; set; }
    public string? Notes { get; set; }
}

public enum BillingStatus
{
    Active = 0,
    Suspended = 1,
    Cancelled = 2
}

public enum PlanType
{
    Trial = 0,
    Basic = 1,
    Pro = 2
}
