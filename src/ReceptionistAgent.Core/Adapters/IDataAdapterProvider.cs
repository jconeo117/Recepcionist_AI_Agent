using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using Microsoft.Extensions.Logging;

namespace ReceptionistAgent.Core.Adapters;

/// <summary>
/// Provee una estrategia para crear componentes de acceso a datos 
/// según el motor de base de datos del tenant.
/// </summary>
public interface IDataAdapterProvider
{
    /// <summary>
    /// Indica si este provider soporta el tipo de base de datos especificado.
    /// </summary>
    bool Supports(string dbType);

    /// <summary>
    /// Crea el adapter principal para datos del cliente (bookings, providers).
    /// </summary>
    IClientDataAdapter CreateAdapter(string connectionString, IBookingBackupService backupService, ILoggerFactory loggerFactory);

    /// <summary>
    /// Crea el repositorio de sesiones de chat.
    /// </summary>
    IChatSessionRepository CreateChatSessionRepository(string coreConnectionString, string? tenantConnectionString, ILoggerFactory loggerFactory);

    /// <summary>
    /// Crea el servicio de recordatorios.
    /// </summary>
    IReminderService CreateReminderService(string coreConnectionString, string? tenantConnectionString);
}
