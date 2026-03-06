using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Services;

/// <summary>
/// Servicio de control de acceso por facturación.
/// Verifica si un tenant tiene acceso activo al agente.
/// La lógica detallada de cobros se implementará después.
/// </summary>
public interface IBillingService
{
    Task<TenantBilling?> GetBillingAsync(string tenantId);

    /// <summary>
    /// Verifica si el tenant tiene acceso: BillingStatus == Active && ActiveUntil > now (o null).
    /// </summary>
    Task<bool> IsTenantAllowedAsync(string tenantId);

    Task SuspendTenantAsync(string tenantId, string reason);
    Task ReactivateTenantAsync(string tenantId, DateTime activeUntil);
    Task UpdateBillingAsync(TenantBilling billing);
    Task CreateBillingAsync(TenantBilling billing);
}
