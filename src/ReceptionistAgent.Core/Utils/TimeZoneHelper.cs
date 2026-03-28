namespace ReceptionistAgent.Core.Utils;

/// <summary>
/// Infiere la zona horaria del cliente a partir del código de país del número de teléfono.
/// Mapea los códigos de llamada internacional más comunes a zonas horarias IANA.
/// Para países con múltiples zonas, se usa la más común/capital.
/// </summary>
public static class TimeZoneHelper
{
    private static readonly Dictionary<string, string> CountryCodeToTimeZone = new(StringComparer.Ordinal)
    {
        // Latinoamérica
        ["57"] = "America/Bogota",       // Colombia
        ["52"] = "America/Mexico_City",  // México
        ["54"] = "America/Argentina/Buenos_Aires", // Argentina
        ["56"] = "America/Santiago",     // Chile
        ["51"] = "America/Lima",         // Perú
        ["58"] = "America/Caracas",      // Venezuela
        ["593"] = "America/Guayaquil",   // Ecuador
        ["591"] = "America/La_Paz",      // Bolivia
        ["595"] = "America/Asuncion",    // Paraguay
        ["598"] = "America/Montevideo",  // Uruguay
        ["506"] = "America/Costa_Rica",  // Costa Rica
        ["507"] = "America/Panama",      // Panamá
        ["503"] = "America/El_Salvador", // El Salvador
        ["502"] = "America/Guatemala",   // Guatemala
        ["504"] = "America/Tegucigalpa", // Honduras
        ["505"] = "America/Managua",     // Nicaragua
        ["809"] = "America/Santo_Domingo", // República Dominicana
        ["1809"] = "America/Santo_Domingo",
        ["53"] = "America/Havana",       // Cuba

        // Norteamérica
        ["1"] = "America/New_York",      // USA/Canadá (default Eastern)

        // Europa
        ["34"] = "Europe/Madrid",        // España
        ["33"] = "Europe/Paris",         // Francia
        ["49"] = "Europe/Berlin",        // Alemania
        ["44"] = "Europe/London",        // Reino Unido
        ["39"] = "Europe/Rome",          // Italia
        ["351"] = "Europe/Lisbon",       // Portugal
        ["31"] = "Europe/Amsterdam",     // Países Bajos
        ["32"] = "Europe/Brussels",      // Bélgica
        ["41"] = "Europe/Zurich",        // Suiza
        ["43"] = "Europe/Vienna",        // Austria

        // Asia
        ["91"] = "Asia/Kolkata",         // India
        ["81"] = "Asia/Tokyo",           // Japón
        ["86"] = "Asia/Shanghai",        // China
        ["82"] = "Asia/Seoul",           // Corea del Sur
        ["971"] = "Asia/Dubai",          // EAU

        // Oceanía
        ["61"] = "Australia/Sydney",     // Australia
        ["64"] = "Pacific/Auckland",     // Nueva Zelanda

        // África
        ["27"] = "Africa/Johannesburg",  // Sudáfrica
        ["234"] = "Africa/Lagos",        // Nigeria
        ["20"] = "Africa/Cairo",         // Egipto

        // Brasil
        ["55"] = "America/Sao_Paulo",    // Brasil
    };

    /// <summary>
    /// Extrae el código de país del número de teléfono y devuelve la zona horaria IANA correspondiente.
    /// Soporta formatos: +573001234567, whatsapp:+573001234567, 573001234567
    /// </summary>
    /// <param name="phoneNumber">Número de teléfono del cliente.</param>
    /// <param name="fallbackTimeZoneId">Zona horaria por defecto si no se puede resolver.</param>
    /// <returns>ID de zona horaria IANA.</returns>
    public static string InferTimeZoneFromPhone(string phoneNumber, string fallbackTimeZoneId = "UTC")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return fallbackTimeZoneId;

        // Limpiar: quitar "whatsapp:", espacios, guiones, paréntesis, "+"
        var cleaned = phoneNumber
            .Replace("whatsapp:", "", StringComparison.OrdinalIgnoreCase)
            .Replace("+", "")
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("(", "")
            .Replace(")", "")
            .Trim();

        if (string.IsNullOrEmpty(cleaned) || cleaned.Length < 7)
            return fallbackTimeZoneId;

        // Intentar coincidencia de mayor a menor especificidad (4 → 3 → 2 → 1 dígitos)
        for (int len = Math.Min(4, cleaned.Length); len >= 1; len--)
        {
            var prefix = cleaned[..len];
            if (CountryCodeToTimeZone.TryGetValue(prefix, out var tz))
                return tz;
        }

        return fallbackTimeZoneId;
    }

    /// <summary>
    /// Convierte la hora UTC actual a la hora local del cliente usando su número de teléfono.
    /// </summary>
    public static DateTime GetClientLocalTime(string phoneNumber, string fallbackTimeZoneId = "UTC")
    {
        var tzId = InferTimeZoneFromPhone(phoneNumber, fallbackTimeZoneId);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
