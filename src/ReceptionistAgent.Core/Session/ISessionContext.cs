namespace ReceptionistAgent.Core.Session;

/// <summary>
/// Contexto de sesión que rastrea las identidades validadas del cliente
/// durante una conversación o request. Permite verificar ownership de reservas.
/// </summary>
public interface ISessionContext
{
    /// <summary>
    /// ID del cliente validado en esta sesión (documento de identidad).
    /// </summary>
    string? ValidatedClientId { get; }

    /// <summary>
    /// ID de la sesión de chat actual.
    /// </summary>
    Guid SessionId { get; set; }

    /// <summary>
    /// Códigos de confirmación que han sido validados en esta sesión.
    /// </summary>
    HashSet<string> ValidatedConfirmationCodes { get; }

    /// <summary>
    /// Marca un ID de cliente como validado en esta sesión.
    /// </summary>
    void ValidateClientId(string clientId);

    /// <summary>
    /// Marca un código de confirmación como validado en esta sesión.
    /// </summary>
    void ValidateConfirmationCode(string code);

    /// <summary>
    /// Verifica si un ID de cliente fue validado en esta sesión.
    /// </summary>
    bool IsClientValidated(string clientId);

    /// <summary>
    /// Verifica si un código de confirmación fue validado en esta sesión.
    /// </summary>
    bool IsCodeValidated(string code);
}
