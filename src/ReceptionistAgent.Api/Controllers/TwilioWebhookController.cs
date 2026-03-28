using Microsoft.AspNetCore.Mvc;
using ReceptionistAgent.AI.Agents;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Security;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Api.Services;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Utils;
using System.Security.Cryptography;
using System.Text;
using Twilio.AspNet.Common;
using Microsoft.Extensions.DependencyInjection;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Microsoft.AspNetCore.RateLimiting;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/twilio/{tenantId}")]
[EnableRateLimiting("Global")]
public class TwilioWebhookController : TwilioController
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TenantContext _tenantContext;

    public TwilioWebhookController(
        IServiceScopeFactory scopeFactory,
        TenantContext tenantContext)
    {
        _scopeFactory = scopeFactory;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    // [ValidateRequest] // Filtro de seguridad de Twilio. COMENTADO PARA PRUEBAS LOCALES.
    public TwiMLResult Webhook([FromRoute] string tenantId, [FromForm] SmsRequest request)
    {
        if (!_tenantContext.IsResolved)
        {
            return TwiMLMessage("Lo siento, no puedo procesar la solicitud en este momento (Tenant no encontrado).");
        }

        var message = request.Body?.Trim() ?? string.Empty;
        var phone = request.From?.Trim() ?? string.Empty; // Twilio "From" (ej: whatsapp:+123456789)

        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(phone))
        {
            return TwiMLMessage("Mensaje inválido.");
        }

        // Mapeo determinístico: Teléfono + Tenant -> Guid de SessionId
        var sessionId = GenerateSessionId(tenantId, phone);
        var clientTimeZone = TimeZoneHelper.InferTimeZoneFromPhone(phone, _tenantContext.CurrentTenant?.TimeZoneId ?? "UTC");
        var messageId = request.MessageSid; // Twilio Message SID for deduplication

        // ═══ Ejecutar pipeline en segundo plano para evitar timeouts de Twilio ═══
        var currentTenant = _tenantContext.CurrentTenant;
        _ = Task.Run(async () => 
        {
            try 
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IChatOrchestrator>();
                var messageFactory = scope.ServiceProvider.GetRequiredService<IMessageSenderFactory>();
                var tContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
                
                // Re-hidratar contexto en el nuevo scope
                tContext.CurrentTenant = currentTenant;

                var result = await orchestrator.ProcessMessageAsync(
                    message: message,
                    sessionId: sessionId,
                    tenantId: tenantId,
                    eventTypePrefix: "WhatsApp",
                    additionalMetadata: new Dictionary<string, string>
                    {
                        ["phone"] = phone,
                        ["clientTimeZone"] = clientTimeZone
                    },
                    messageId: messageId
                );

                if (currentTenant != null)
                {
                    var sender = messageFactory.CreateSender(currentTenant);
                    await sender.SendAsync(phone, result.Response);
                }
            }
            catch (Exception ex)
            {
                using var scope = _scopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<TwilioWebhookController>>();
                logger.LogError(ex, "Error in Twilio background processing");
            }
        });

        return TwiMLMessage(""); // Respuesta vacía inmediata, la real se envía asíncronamente
    }

    private TwiMLResult TwiMLMessage(string message)
    {
        var response = new MessagingResponse();
        response.Message(message);
        return TwiML(response);
    }

    /// <summary>
    /// Convierte la combinación de TenantId y teléfono de forma determinística en un Guid.
    /// Así el mismo número de teléfono tiene un SessionId aislado por cada Tenant.
    /// </summary>
    private static Guid GenerateSessionId(string tenantId, string phone)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"WhatsAppSessionSalt_{tenantId}_{phone}"));
        return new Guid(hash);
    }
}
