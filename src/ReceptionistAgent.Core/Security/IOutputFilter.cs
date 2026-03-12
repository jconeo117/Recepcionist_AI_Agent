namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Resultado del filtrado de una respuesta del agente.
/// </summary>
public record FilterResult(string FilteredContent, bool WasModified, List<string> RedactedItems);

/// <summary>
/// Filtra las respuestas del agente antes de enviarlas al usuario,
/// eliminando PII y contenido que pueda revelar datos sensibles.
/// </summary>
public interface IOutputFilter
{
    Task<FilterResult> FilterAsync(string agentResponse, string tenantId, IEnumerable<string>? allowedPhones = null, IEnumerable<string>? whitelistedTexts = null);
}
