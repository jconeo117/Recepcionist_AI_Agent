using System.Text.RegularExpressions;

namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Detecta intentos de prompt injection, jailbreak y extracción de datos
/// usando análisis basado en patrones (EN + ES).
/// </summary>
public class PromptInjectionGuard : IInputGuard
{
    // Patrones de alta amenaza: intentos directos de override o data exfiltration
    private static readonly (Regex Pattern, string Reason)[] HighThreatPatterns =
    [
        // Role override attempts
        (new Regex(@"ignor[ae]\s+(todas\s+)?(las\s+)?(instrucciones|reglas|restricciones)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de anular instrucciones del sistema"),
        (new Regex(@"ignore\s+(all\s+)?(previous|prior|above)\s+(instructions|rules|prompts)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Attempt to override system instructions"),
        (new Regex(@"(olvida|ignora|descarta)\s+(todas?\s+)?(los\s+|las\s+|tus\s+)?(instrucciones|reglas|directivas|indicaciones)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de descartar directivas del sistema"),
        (new Regex(@"(act[uú]a|compórtate|finge|pretende)\s+(como|ser|que\s+eres)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de cambio de rol"),
        (new Regex(@"(you\s+are\s+now|from\s+now\s+on\s+you\s+are|new\s+role|switch\s+to)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Attempt to reassign agent role"),

        // System prompt extraction
        (new Regex(@"(mu[eé]stra(me)?|dime|revela|comparte|repite|copia)\s+(tu|el|las?)\s+(prompt|instrucciones|system\s*prompt|configuraci[oó]n)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de extraer el system prompt"),
        (new Regex(@"(show|reveal|display|print|repeat|share)\s+(your\s+)?(the\s+)?(system\s*)?(prompt|instructions|config)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Attempt to extract system prompt"),
        (new Regex(@"cu[aá]les\s+son\s+tus\s+(instrucciones|reglas|directivas)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de extraer instrucciones"),

        // Data exfiltration attempts
        (new Regex(@"(lista|muestra|dame|dime)\s+(todos?\s+)?(los\s+)?(nombres|pacientes|clientes|citas|datos)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de extracción masiva de datos"),
        (new Regex(@"(list|show|give|tell)\s+(me\s+)?(all\s+)?(patients|clients|names|appointments|bookings|data)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Attempt to extract bulk data"),

        // DAN / jailbreak patterns
        (new Regex(@"\bDAN\b|do\s+anything\s+now|jailbreak", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Patrón de jailbreak DAN detectado"),
    ];

    // Patrones de amenaza media: sondeos sospechosos
    private static readonly (Regex Pattern, string Reason)[] MediumThreatPatterns =
    [
        (new Regex(@"(qu[eé]\s+puedes\s+hacer\s+realmente|cu[aá]les\s+son\s+tus\s+l[ií]mites)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Sondeo de límites del sistema"),
        (new Regex(@"(tienes\s+acceso\s+a|puedes\s+ver|puedes\s+acceder)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Sondeo de capacidades de acceso a datos"),
        (new Regex(@"(modo\s+desarrollo|modo\s+debug|developer\s+mode|test\s+mode|admin\s+mode)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de activar modo especial"),
        (new Regex(@"(simula|sim[uú]la|hypothetically|hipot[eé]ticamente|imagina\s+que)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Intento de evasión hipotética"),
        (new Regex(@"(cuenta\s+(desde|hasta)|cu[eé]ntame\s+un\s+chiste|escribe\s+un\s+poema|resuelve\s+esto|traduce)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Patrón de off-topic o trolling detectado"),
    ];

    // Respuesta genérica que mantiene al agente en su rol
    private const string GenericRejection =
        "Solo puedo ayudarle con la gestión de citas y consultas sobre nuestros servicios. ¿Desea agendar una cita o tiene alguna consulta administrativa?";

    public Task<GuardResult> AnalyzeAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult(new GuardResult(true, null, ThreatLevel.None));

        if (userMessage.Length > 800)
            return Task.FromResult(new GuardResult(false, "El mensaje es demasiado largo. Por favor, sea más breve.", ThreatLevel.High));

        var normalized = userMessage.Trim();

        // Verificar patrones de alta amenaza
        foreach (var (pattern, reason) in HighThreatPatterns)
        {
            if (pattern.IsMatch(normalized))
            {
                return Task.FromResult(new GuardResult(false, GenericRejection, ThreatLevel.High));
            }
        }

        // Verificar patrones de amenaza media
        foreach (var (pattern, reason) in MediumThreatPatterns)
        {
            if (pattern.IsMatch(normalized))
            {
                return Task.FromResult(new GuardResult(false, GenericRejection, ThreatLevel.Medium));
            }
        }

        // Mensaje largo con muchas instrucciones => sospechoso
        if (normalized.Length > 1000 && ContainsMultipleInstructions(normalized))
        {
            return Task.FromResult(new GuardResult(false, GenericRejection, ThreatLevel.Medium));
        }

        return Task.FromResult(new GuardResult(true, null, ThreatLevel.None));
    }

    private static bool ContainsMultipleInstructions(string text)
    {
        var instructionPatterns = new[]
        {
            @"\bstep\s*\d+\b", @"\bpaso\s*\d+\b",
            @"\b(primero|segundo|tercero|luego|despu[eé]s)\b",
            @"\b(first|second|third|then|next|finally)\b"
        };

        var count = instructionPatterns
            .Sum(p => Regex.Matches(text, p, RegexOptions.IgnoreCase).Count);

        return count >= 3;
    }
}
