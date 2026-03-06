namespace ReceptionistAgent.Core.Services;

/// <summary>
/// Envía mensajes outbound al usuario (ej: recordatorios por WhatsApp).
/// </summary>
public interface IMessageSender
{
    /// <summary>
    /// Envía un mensaje de texto al destinatario.
    /// </summary>
    /// <param name="to">Número de teléfono destino (ej: whatsapp:+573001234567)</param>
    /// <param name="message">Contenido del mensaje</param>
    /// <returns>true si se envió con éxito</returns>
    Task<bool> SendAsync(string to, string message);
}
