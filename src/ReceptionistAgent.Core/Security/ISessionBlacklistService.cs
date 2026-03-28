using System;
using System.Threading.Tasks;

namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Interfaz para gestionar la lista negra de sesiones maliciosas.
/// </summary>
public interface ISessionBlacklistService
{
    /// <summary>
    /// Verifica si una sesión está en la lista negra.
    /// </summary>
    Task<bool> IsBlacklistedAsync(Guid sessionId);

    /// <summary>
    /// Agrega una sesión a la lista negra por una duración específica.
    /// </summary>
    Task BlacklistSessionAsync(Guid sessionId, TimeSpan duration);
}
