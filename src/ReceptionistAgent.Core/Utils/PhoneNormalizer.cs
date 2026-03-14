namespace ReceptionistAgent.Core.Utils;

/// <summary>
/// Normaliza números de teléfono a formato E.164 limpio (solo dígitos, sin '+').
/// Añade el código de país del tenant si el número no lo incluye.
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>
    /// Normaliza un número de teléfono a dígitos puros con código de país.
    /// Ejemplos:
    ///   "whatsapp:+573245058320", "57" → "573245058320"
    ///   "3245058320",             "57" → "573245058320"
    ///   "+573245058320",          "57" → "573245058320"
    ///   "573245058320",           "57" → "573245058320"
    /// </summary>
    public static string Normalize(string phone, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return phone;

        // 1. Quitar prefijo whatsapp: y +
        var digits = phone
            .Replace("whatsapp:", "")
            .Replace("+", "")
            .Trim();

        // 2. Quitar caracteres no numéricos (guiones, espacios, paréntesis)
        digits = new string(digits.Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(digits))
            return phone; // no se pudo normalizar, devolver original

        // 3. Añadir código de país si no lo tiene
        var cc = (countryCode ?? "").Trim().TrimStart('+');
        if (!string.IsNullOrWhiteSpace(cc) && !digits.StartsWith(cc))
        {
            digits = cc + digits;
        }

        return digits;
    }

    /// <summary>
    /// Devuelve el número en formato whatsapp:+E164, listo para Twilio.
    /// </summary>
    public static string ToTwilioFormat(string phone, string countryCode)
        => $"whatsapp:+{Normalize(phone, countryCode)}";

    /// <summary>
    /// Devuelve solo dígitos con código de país, listo para Meta Graph API.
    /// </summary>
    public static string ToMetaFormat(string phone, string countryCode)
        => Normalize(phone, countryCode);
}