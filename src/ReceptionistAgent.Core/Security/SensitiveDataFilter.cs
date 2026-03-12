using System.Text.RegularExpressions;

namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Filtra respuestas del agente para:
/// 1. Detectar y enmascarar PII (emails, teléfonos, cédulas)
/// 2. Bloquear respuestas que revelen el system prompt
/// 3. Detectar si el agente salió de su rol
/// </summary>
public class SensitiveDataFilter : IOutputFilter
{
    // Patrones PII para enmascarar
    private static readonly (Regex Pattern, string Replacement, string Label)[] PiiPatterns =
    [
        // Email
        (new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "[EMAIL PROTEGIDO]", "email"),

        // Teléfono colombiano (varios formatos)
        (new Regex(@"\b(?:(?:\+57|57)\s?)?(?:3\d{2}[\s.-]?\d{3}[\s.-]?\d{4}|(?:\d{3})[\s.-]?\d{3}[\s.-]?\d{4})\b", RegexOptions.Compiled),
         "[TELÉFONO PROTEGIDO]", "phone"),

        // Cédula colombiana (6-10 dígitos solos, contexto de documento)
        (new Regex(@"(?:c[eé]dula|CC|documento|DNI|identificaci[oó]n)[:\s#]*\d{6,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "[DOCUMENTO PROTEGIDO]", "document_id"),
    ];

    // Patrones que indican fuga del system prompt
    private static readonly Regex[] PromptLeakPatterns =
    [
        new(@"# IDENTIDAD Y CONTEXTO", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"RESTRICCI[OÓ]N PROFESIONAL ABSOLUTA", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"PROTOCOLO DE SEGURIDAD", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"INSTRUCCIONES DE SEGURIDAD INMUTABLES", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"BookingPlugin[-_]\w+|BusinessInfoPlugin[-_]\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"KernelFunction|ToolCallBehavior|AutoInvokeKernel", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // Patrones que sugieren que el agente salió de su rol
    private static readonly Regex[] RoleViolationPatterns =
    [
        new(@"como\s+(modelo|inteligencia\s+artificial|IA|AI)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(as\s+an?\s+(AI|language\s+model)|I('m|\s+am)\s+an?\s+(AI|language\s+model))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"seg[uú]n\s+mi\s+entrenamiento", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private const string SafeRoleResponse =
        "Disculpe, ¿puedo ayudarle con alguna consulta sobre nuestros servicios o desea agendar una cita?";

    public Task<FilterResult> FilterAsync(string agentResponse, string tenantId, IEnumerable<string>? allowedPhones = null, IEnumerable<string>? whitelistedTexts = null)
    {
        if (string.IsNullOrWhiteSpace(agentResponse))
            return Task.FromResult(new FilterResult(agentResponse, false, []));

        var redactedItems = new List<string>();
        var filtered = agentResponse;
        var wasModified = false;

        // 1. Verificar fuga de system prompt
        foreach (var pattern in PromptLeakPatterns)
        {
            if (pattern.IsMatch(filtered))
            {
                redactedItems.Add("prompt_leak");
                return Task.FromResult(new FilterResult(SafeRoleResponse, true, redactedItems));
            }
        }

        // 2. Verificar violación de rol
        foreach (var pattern in RoleViolationPatterns)
        {
            if (pattern.IsMatch(filtered))
            {
                redactedItems.Add("role_violation");
                return Task.FromResult(new FilterResult(SafeRoleResponse, true, redactedItems));
            }
        }

        // 3. Preparar reemplazos temporales para proteger datos permitidos (Teléfonos del negocio y Whitelist)
        var temporaryReplacements = new Dictionary<string, string>();
        int placeholderIdx = 0;

        // --- Proteger teléfonos del negocio ---
        if (allowedPhones != null)
        {
            foreach (var phone in allowedPhones.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var placeholder = $"__PROTECTED_ENTRY_{placeholderIdx++}__";
                if (filtered.Contains(phone))
                {
                    filtered = filtered.Replace(phone, placeholder);
                    temporaryReplacements[placeholder] = phone;
                }
            }
        }

        // --- Proteger textos de la lista blanca (ej: datos que el usuario acaba de dar) ---
        if (whitelistedTexts != null)
        {
            foreach (var text in whitelistedTexts.Where(t => !string.IsNullOrWhiteSpace(t) && t.Length > 3))
            {
                var placeholder = $"__PROTECTED_ENTRY_{placeholderIdx++}__";
                if (filtered.Contains(text))
                {
                    filtered = filtered.Replace(text, placeholder);
                    temporaryReplacements[placeholder] = text;
                }
            }
        }

        // 4. Enmascarar PII sobre lo que queda
        foreach (var (pattern, replacement, label) in PiiPatterns)
        {
            if (pattern.IsMatch(filtered))
            {
                filtered = pattern.Replace(filtered, replacement);
                redactedItems.Add(label);
                wasModified = true;
            }
        }

        // 5. Restaurar datos protegidos
        foreach (var (placeholder, originalValue) in temporaryReplacements)
        {
            filtered = filtered.Replace(placeholder, originalValue);
        }

        return Task.FromResult(new FilterResult(filtered, wasModified, redactedItems));
    }
}
