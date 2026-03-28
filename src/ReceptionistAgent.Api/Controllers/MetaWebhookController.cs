using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using ReceptionistAgent.Api.Services;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Core.Utils;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/webhook/meta")]
public class MetaWebhookController : ControllerBase
{
    private readonly ILogger<MetaWebhookController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _verifyToken;

    public MetaWebhookController(
        ILogger<MetaWebhookController> logger,
        IConfiguration config,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _verifyToken = config["Meta:VerifyToken"] ?? "agente_secreto_2026";
    }

    /// <summary>
    /// Meta verifica el webhook con un GET al registrarlo en el Developer Portal.
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        _logger.LogInformation("Meta webhook verification. Mode: {Mode}", mode);

        if (mode == "subscribe" && token == _verifyToken)
        {
            _logger.LogInformation("Meta webhook verified successfully.");
            return Ok(challenge);
        }

        _logger.LogWarning("Meta webhook verification failed. Token mismatch.");
        return Forbid();
    }

    /// <summary>
    /// Recibe mensajes entrantes de WhatsApp Cloud API.
    /// Meta espera un 200 OK inmediato — el procesamiento ocurre en background.
    /// </summary>
    [HttpPost]
    public IActionResult ReceiveMessage([FromBody] JsonElement body)
    {
        try
        {
            if (body.TryGetProperty("object", out var objProp) &&
                objProp.GetString() == "whatsapp_business_account")
            {
                // Meta expects a 200 OK within 10 seconds.
                // We fire and forget the processing logic to avoid timeouts and retries.
                _ = Task.Run(async () => await ProcessPayloadAsync(body)); 
                return Ok();
            }

            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Meta webhook payload.");
            return StatusCode(500);
        }
    }

    // ─── Procesamiento en background ─────────────────────────────────────────

    private async Task ProcessPayloadAsync(JsonElement body)
    {
        try
        {
            var entries = body.GetProperty("entry").EnumerateArray();
            foreach (var entry in entries)
            {
                foreach (var change in entry.GetProperty("changes").EnumerateArray())
                {
                    var value = change.GetProperty("value");
                    if (!value.TryGetProperty("messages", out var messages)) continue;

                    // El phone_number_id identifica a qué número de negocio llegó el mensaje
                    var phoneNumberId = value
                        .GetProperty("metadata")
                        .GetProperty("phone_number_id")
                        .GetString();

                    foreach (var message in messages.EnumerateArray())
                    {
                        if (!message.TryGetProperty("type", out var typeProp) ||
                            typeProp.GetString() != "text") continue;

                        var messageId = message.GetProperty("id").GetString();
                        var fromPhone = message.GetProperty("from").GetString();
                        var text = message.GetProperty("text").GetProperty("body").GetString();

                        if (string.IsNullOrEmpty(fromPhone) ||
                            string.IsNullOrEmpty(text) ||
                            string.IsNullOrEmpty(phoneNumberId)) continue;

                        _logger.LogInformation(
                            "Meta message {MsgId} from {From} via PhoneId {PhoneId}: {Text}",
                            messageId, fromPhone, phoneNumberId, text);

                        await OrchestrateAsync(fromPhone, text, phoneNumberId, messageId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background error processing Meta payload.");
        }
    }

    private async Task OrchestrateAsync(string fromPhone, string text, string phoneNumberId, string? messageId = null)
    {
        using var scope = _scopeFactory.CreateScope();

        // PASO 1: resolver tenant y asignar al TenantContext ANTES de resolver
        // cualquier servicio que dependa de él (IClientDataAdapter, IChatOrchestrator, etc.)
        var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        // 5) Identificar tenant usando la interfaz nativa extendida (ITenantResolver)
        var tenantConfig = await tenantResolver.ResolveByMetaPhoneNumberIdAsync(phoneNumberId);

        if (tenantConfig == null)
        {
            var all = await tenantResolver.GetAllTenantsAsync();
            tenantConfig = all.FirstOrDefault(t =>
                t.MessageProvider.Equals("Meta", StringComparison.OrdinalIgnoreCase) &&
                t.MessageProviderAccount == phoneNumberId);
        }

        if (tenantConfig == null)
        {
            _logger.LogError(
                "No tenant found for Meta PhoneNumberId {PhoneId}. " +
                "Verify that MessageProviderAccount is set correctly in the DB.", phoneNumberId);
            return;
        }

        // PASO 2: tenant en contexto — ahora el DI puede construir IClientDataAdapter
        tenantContext.CurrentTenant = tenantConfig;
        _logger.LogInformation("Tenant Context manually set to {TenantId}", tenantConfig.TenantId);

        // PASO 3: recién ahora resolver servicios que dependen del TenantContext
        var orchestrator = scope.ServiceProvider.GetRequiredService<IChatOrchestrator>();
        var messageFactory = scope.ServiceProvider.GetRequiredService<IMessageSenderFactory>();

        var sessionId = GenerateSessionId(tenantConfig.TenantId, fromPhone);
        var clientTimeZone = TimeZoneHelper.InferTimeZoneFromPhone(fromPhone, tenantConfig.TimeZoneId ?? "UTC");
        var metadata = new Dictionary<string, string>
        {
            { "phone", fromPhone },
            { "clientTimeZone", clientTimeZone }
        };

        var result = await orchestrator.ProcessMessageAsync(
            message: text,
            sessionId: sessionId,
            tenantId: tenantConfig.TenantId,
            eventTypePrefix: "Meta",
            additionalMetadata: metadata,
            messageId: messageId);

        var sender = messageFactory.CreateSender(tenantConfig);
        await sender.SendAsync(fromPhone, result.Response);

        _logger.LogInformation(
            "Meta response sent to {To} (tenant: {TenantId})", fromPhone, tenantConfig.TenantId);
    }

    private static Guid GenerateSessionId(string tenantId, string phone)
    {
        var input = $"MetaSessionSalt_{tenantId}_{phone}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}