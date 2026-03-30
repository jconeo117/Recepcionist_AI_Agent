using System.Threading.Tasks;

namespace ReceptionistAgent.Core.Services;

public interface IWebhookDeduplicator
{
    /// <summary>
    /// Intenta registrar un mensaje como procesado.
    /// Retorna false si el mensaje ya existe (es un duplicado o reintento).
    /// </summary>
    Task<bool> TryRegisterMessageAsync(string messageId, string tenantId);
}
