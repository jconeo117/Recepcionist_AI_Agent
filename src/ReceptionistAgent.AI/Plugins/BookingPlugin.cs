using System.ComponentModel;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Session;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace ReceptionistAgent.AI.Plugins;

public class BookingPlugin
{
    private readonly IBookingService _bookingService;
    private readonly ISessionContext _sessionContext;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<BookingPlugin> _logger;

    public BookingPlugin(
        IBookingService bookingService,
        ISessionContext sessionContext,
        TenantContext tenantContext,
        ILogger<BookingPlugin> logger)
    {
        _bookingService = bookingService;
        _sessionContext = sessionContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private DateTime GetTenantCurrentDate()
    {
        var tzId = _tenantContext.CurrentTenant?.TimeZoneId ?? "UTC";
        try
        {
            var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzInfo).Date;
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogWarning(ex, "Zona horaria '{TimeZoneId}' no encontrada para el tenant actual. Usando fecha UTC como respaldo.", tzId);
            return DateTime.UtcNow.Date; // Fallback to UTC if timezone is invalid
        }
    }

    [KernelFunction]
    [Description("Busca horarios disponibles. Puede buscar por nombre de proveedor, rol/especialidad, o mostrar todos los disponibles")]
    public async Task<string> FindAvailableSlots(
        [Description("Nombre del proveedor (ej: 'Ramírez', 'Carlos'), rol (ej: 'oftalmología', 'retina'), o 'cualquiera' para ver todos")] string providerQuery,
        [Description("Fecha en formato YYYY-MM-DD")] string stringDate)
    {
        if (!DateTime.TryParse(stringDate, out var date))
            return "Por favor, usar el formato YYYY-MM-DD";

        List<ServiceProvider> matchingProviders;

        if (providerQuery.Equals("cualquiera", StringComparison.OrdinalIgnoreCase))
        {
            matchingProviders = await _bookingService.GetAllProvidersAsync();
        }
        else
        {
            matchingProviders = await _bookingService.SearchProvidersAsync(providerQuery);
        }

        if (!matchingProviders.Any())
        {
            var allProviders = await _bookingService.GetAllProvidersAsync();
            return $"No se encontró proveedor que coincida con '{providerQuery}'. Disponibles: {string.Join(", ", allProviders.Select(p => p.Name))}";
        }

        var results = new List<string>();

        foreach (var provider in matchingProviders)
        {
            var slots = await _bookingService.GetAvailableSlotsAsync(provider.Id, date);
            var availableSlots = slots.Where(s => s.IsAvailable).ToList();

            if (availableSlots.Any())
            {
                var times = availableSlots.Select(s => s.Time.ToString(@"hh\:mm")).ToList();
                results.Add($"• {provider.Name} ({provider.Role}): {string.Join(", ", times)}");
            }
        }

        if (!results.Any())
            return $"No hay horarios disponibles para {date:yyyy-MM-dd}";

        return $"Horarios disponibles para {date:yyyy-MM-dd}:\n{string.Join("\n", results)}";
    }

    [KernelFunction]
    [Description("Busca la primera cita disponible con cualquier proveedor desde hoy hacia adelante")]
    public async Task<string> GetFirstAvailableAppointment(
        [Description("Número de días hacia adelante a buscar (default: 30)")] int daysToSearch = 30)
    {
        var today = GetTenantCurrentDate();
        var allProviders = await _bookingService.GetAllProvidersAsync();

        for (int i = 0; i < daysToSearch; i++)
        {
            var date = today.AddDays(i);

            foreach (var provider in allProviders)
            {
                var slots = await _bookingService.GetAvailableSlotsAsync(provider.Id, date);
                var availableSlots = slots.Where(s => s.IsAvailable).ToList();

                if (availableSlots.Any())
                {
                    var times = availableSlots.Select(s => s.Time.ToString(@"hh\:mm")).Take(5).ToList();
                    return $"Primera cita disponible:\n" +
                           $"Proveedor: {provider.Name} ({provider.Role})\n" +
                           $"Fecha: {date:yyyy-MM-dd} ({date:dddd})\n" +
                           $"Horarios: {string.Join(", ", times)}";
                }
            }
        }

        return $"No hay disponibilidad en los próximos {daysToSearch} días";
    }

    [KernelFunction]
    [Description("Agenda una nueva cita. Si faltan datos, devuelve un error descriptivo. El documento de identidad del cliente es OBLIGATORIO.")]
    public async Task<string> BookAppointment(
        [Description("Nombre completo del cliente")] string clientName,
        [Description("Documento de identidad del cliente (cédula, DNI, etc.) - REQUERIDO")] string clientId,
        [Description("Telefono celular del cliente")] string clientPhone,
        [Description("Correo electronico del cliente (Opcional, usar 'no-email' si no se tiene)")] string clientEmail,
        [Description("Nombre del proveedor (ej: 'Dr. Ramírez') o ID (ej: 'DR001')")] string providerNameOrId,
        [Description("Fecha para agendar la cita. Formato YYYY-MM-DD")] string stringDate,
        [Description("Horario para agendar la cita. Formato 24 horas HH:MM")] string stringTime,
        [Description("Razon de la cita")] string reason)
    {
        var invalidTerms = new[] { "no email", "no-email", "unknown", "no nombre", "string", "user", "no phone", "no-id", "no id" };

        if (string.IsNullOrWhiteSpace(clientName) || invalidTerms.Any(t => clientName.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return "FALLO DE VALIDACIÓN: Falta el NOMBRE del cliente. NO inventes valores. PREGUNTA al usuario su nombre.";

        if (string.IsNullOrWhiteSpace(clientId) || invalidTerms.Any(t => clientId.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return "FALLO DE VALIDACIÓN: Falta el DOCUMENTO DE IDENTIDAD del cliente. PREGUNTA al usuario su cédula o documento.";

        if (string.IsNullOrWhiteSpace(clientEmail) || !clientEmail.Contains('@') || invalidTerms.Any(t => clientEmail.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return "FALLO DE VALIDACIÓN: Falta un EMAIL válido. NO uses 'no-email'. PREGUNTA al usuario su correo.";

        if (string.IsNullOrWhiteSpace(clientPhone) || invalidTerms.Any(t => clientPhone.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return "FALLO DE VALIDACIÓN: Falta el TELÉFONO. PREGUNTA al usuario su número.";

        try
        {
            if (!DateTime.TryParse(stringDate, out var date))
                return $"FALLO: La fecha '{stringDate}' no es válida. Usa formato YYYY-MM-DD.";

            if (!TimeSpan.TryParse(stringTime, out var time))
                return $"FALLO: La hora '{stringTime}' no es válida. Usa formato HH:MM (24h).";

            var matchingProviders = await _bookingService.SearchProvidersAsync(providerNameOrId);

            if (matchingProviders.Count == 0)
            {
                var allProviders = await _bookingService.GetAllProvidersAsync();
                return $"FALLO: No se encontró proveedor '{providerNameOrId}'. Disponibles: {string.Join(", ", allProviders.Select(p => p.Name))}";
            }

            if (matchingProviders.Count > 1)
                return $"FALLO: Múltiples proveedores encontrados para '{providerNameOrId}': {string.Join(", ", matchingProviders.Select(p => p.Name))}. Por favor sea más específico.";

            var provider = matchingProviders.First();

            var customFields = new Dictionary<string, object>
            {
                ["clientId"] = clientId,
                ["phone"] = clientPhone,
                ["email"] = clientEmail,
                ["reason"] = reason
            };

            var booking = await _bookingService.CreateBookingAsync(
                clientName,
                provider.Id,
                date,
                time,
                customFields);

            if (booking != null)
            {
                // Auto-validar en el contexto de sesión
                _sessionContext.ValidateClientId(clientId);
                _sessionContext.ValidateConfirmationCode(booking.ConfirmationCode);

                return $"ÉXITO: Cita confirmada exitosamente. \n" +
                       $"Código de Confirmación: {booking.ConfirmationCode} \n" +
                       $"Cliente: {clientName} \n" +
                       $"Documento: {clientId} \n" +
                       $"Proveedor: {provider.Name} ({provider.Role})\n" +
                       $"Fecha: {date:yyyy-MM-dd} a las {time} \n" +
                       $"INSTRUCCIÓN PARA EL AGENTE: Informa al usuario el código de confirmación y recuérdale llegar 15 minutos antes.";
            }

            return "FALLO: El horario seleccionado ya no está disponible.";
        }
        catch (Exception ex)
        {
            return $"ERROR DEL SISTEMA: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Cancelar una cita. Requiere verificación de identidad: el cliente debe haber sido validado previamente en esta sesión.")]
    public async Task<string> CancelAppointment(
        [Description("Codigo de confirmacion de la cita")] string confirmationCode)
    {
        var booking = await _bookingService.GetBookingAsync(confirmationCode);
        if (booking == null)
            return $"La cita con el código {confirmationCode} no fue encontrada, pruebe nuevamente.";

        // Verificar ownership: debe tener el código validado o el clientId validado
        var clientId = booking.CustomFields.TryGetValue("clientId", out var pid) ? pid?.ToString() : null;
        if (!_sessionContext.IsCodeValidated(confirmationCode) &&
            (clientId == null || !_sessionContext.IsClientValidated(clientId)))
        {
            return "ACCESO DENEGADO: No se puede cancelar esta cita. " +
                   "Primero debe verificar su identidad proporcionando su documento de identidad o código de confirmación.";
        }

        var success = await _bookingService.CancelBookingAsync(confirmationCode);

        if (success)
            return $"✓ Cita cancelada: {booking.ClientName}, " +
                   $"{booking.ScheduledDate:yyyy-MM-dd} {booking.ScheduledTime:hh\\:mm}";

        return "Error al cancelar la cita";
    }

    [KernelFunction]
    [Description("Obtener información de una cita. Se puede buscar por código de confirmación O por documento de identidad del cliente. Requiere verificación de ownership.")]
    public async Task<string> GetAppointmentInfo(
        [Description("Código de confirmación de la cita (opcional si se proporciona documento)")] string confirmationCode = "",
        [Description("Documento de identidad del cliente (opcional si se proporciona código)")] string clientId = "")
    {
        BookingRecord? booking = null;

        // Buscar por código de confirmación
        if (!string.IsNullOrWhiteSpace(confirmationCode))
        {
            booking = await _bookingService.GetBookingAsync(confirmationCode);
            if (booking == null)
                return $"La cita con el código {confirmationCode} no fue encontrada, pruebe nuevamente.";

            // Validar ownership: por código o por clientId en sesión
            var bookingClientId = booking.CustomFields.TryGetValue("clientId", out var pid) ? pid?.ToString() : null;

            if (!_sessionContext.IsCodeValidated(confirmationCode) &&
                (bookingClientId == null || !_sessionContext.IsClientValidated(bookingClientId)))
            {
                // Auto-validar si el usuario proporciona un clientId correcto
                if (!string.IsNullOrWhiteSpace(clientId) &&
                    bookingClientId != null &&
                    clientId.Equals(bookingClientId, StringComparison.OrdinalIgnoreCase))
                {
                    _sessionContext.ValidateClientId(clientId);
                    _sessionContext.ValidateConfirmationCode(confirmationCode);
                }
                else
                {
                    return "ACCESO DENEGADO: No se puede verificar la identidad. " +
                           "Proporcione su documento de identidad para verificar que esta cita le pertenece.";
                }
            }
        }
        // Buscar por documento de identidad
        else if (!string.IsNullOrWhiteSpace(clientId))
        {
            booking = await _bookingService.GetBookingByClientIdAsync(clientId);
            if (booking == null)
                return $"No se encontraron citas asociadas al documento {clientId}.";

            // Auto-validar el clientId y el código
            _sessionContext.ValidateClientId(clientId);
            _sessionContext.ValidateConfirmationCode(booking.ConfirmationCode);
        }
        else
        {
            return "FALLO DE VALIDACIÓN: Debe proporcionar un código de confirmación O un documento de identidad.";
        }

        return $"Cita {booking.ConfirmationCode}:\n" +
               $"Cliente: {booking.ClientName}\n" +
               $"Proveedor: {booking.ProviderName}\n" +
               $"Fecha: {booking.ScheduledDate:yyyy-MM-dd}\n" +
               $"Hora: {booking.ScheduledTime:hh\\:mm}\n" +
               $"Estado: {booking.Status}";
    }

    [KernelFunction]
    [Description("Lista la ocupación de citas para hoy (sin datos de clientes por privacidad).")]
    public async Task<string> GetAllAppointmentsByDate()
    {
        var bookings = await _bookingService.GetBookingsByDateAsync(GetTenantCurrentDate());
        if (!bookings.Any())
            return "No hay citas agendadas para hoy";

        // SEGURIDAD: No exponer nombres de clientes
        return $"Citas agendadas para hoy ({bookings.Count} total):\n" +
               string.Join("\n", bookings.Select(b =>
                   $"- {b.ScheduledTime:hh\\:mm} con {b.ProviderName} ({b.Status})"));
    }
}
