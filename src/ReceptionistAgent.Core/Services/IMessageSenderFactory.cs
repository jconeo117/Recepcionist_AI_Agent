using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Services;

/// <summary>
/// Factory responsable de crear el IMessageSender adecuado (Twilio vs Meta)
/// según la configuración del tenant específico.
/// </summary>
public interface IMessageSenderFactory
{
    /// <summary>
    /// Crea una instancia transitoria de IMessageSender basada en la configuración del tenant proporcionado.
    /// </summary>
    IMessageSender CreateSender(TenantConfiguration tenantConfig);

    /// <summary>
    /// Recupera la configuración del tenant por su ID y crea el sender adecuado.
    /// </summary>
    Task<IMessageSender> CreateSenderAsync(string tenantId);
}
