namespace ReceptionistAgent.Core.Session;

/// <summary>
/// Implementación scoped de ISessionContext.
/// Mantiene en memoria los IDs y códigos validados durante un request/sesión.
/// </summary>
public class SessionContext : ISessionContext
{
    private readonly HashSet<string> _validatedClientIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _validatedConfirmationCodes = new(StringComparer.OrdinalIgnoreCase);

    public string? ValidatedClientId { get; private set; }
    public Guid SessionId { get; set; }

    public HashSet<string> ValidatedConfirmationCodes => _validatedConfirmationCodes;

    public void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("El ID del cliente no puede estar vacío.", nameof(clientId));

        ValidatedClientId = clientId;
        _validatedClientIds.Add(clientId);
    }

    public void ValidateConfirmationCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("El código de confirmación no puede estar vacío.", nameof(code));

        _validatedConfirmationCodes.Add(code);
    }

    public bool IsClientValidated(string clientId)
    {
        return !string.IsNullOrWhiteSpace(clientId) &&
               _validatedClientIds.Contains(clientId);
    }

    public bool IsCodeValidated(string code)
    {
        return !string.IsNullOrWhiteSpace(code) &&
               _validatedConfirmationCodes.Contains(code);
    }
}
