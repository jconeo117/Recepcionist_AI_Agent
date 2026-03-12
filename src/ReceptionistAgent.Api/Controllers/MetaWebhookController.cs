using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using ReceptionistAgent.Api.Services;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Models;

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
    /// Endpoint used by Meta to verify the webhook URL.
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        _logger.LogInformation("Receiving Meta verification request. Mode: {Mode}, Token: {Token}", mode, token);

        if (mode == "subscribe" && token == _verifyToken)
        {
            _logger.LogInformation("Meta Webhook verified successfully.");
            return Ok(challenge);
        }

        _logger.LogWarning("Meta Webhook verification failed. Token mismatch or invalid mode.");
        return Forbid();
    }

    /// <summary>
    /// Endpoint to receive incoming messages from WhatsApp Cloud API.
    /// </summary>
    [HttpPost]
    public IActionResult ReceiveMessage([FromBody] JsonElement body)
    {
        try
        {
            _logger.LogDebug("Meta Webhook received: {Body}", body.GetRawText());

            if (body.TryGetProperty("object", out var objProperty) && objProperty.GetString() == "whatsapp_business_account")
            {
                // Respond 200 OK immediately to satisfy Meta's retry policy
                _ = Task.Run(async () => await ProcessIncomingMessageAsync(body));
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

    private async Task ProcessIncomingMessageAsync(JsonElement body)
    {
        try
        {
            var entries = body.GetProperty("entry").EnumerateArray();
            foreach (var entry in entries)
            {
                var changes = entry.GetProperty("changes").EnumerateArray();
                foreach (var change in changes)
                {
                    var value = change.GetProperty("value");
                    if (value.TryGetProperty("messages", out var messages))
                    {
                        var messageArray = messages.EnumerateArray();
                        foreach (var message in messageArray)
                        {
                            if (message.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text")
                            {
                                var fromPhone = message.GetProperty("from").GetString();
                                var text = message.GetProperty("text").GetProperty("body").GetString();
                                var phoneNumberId = value.GetProperty("metadata").GetProperty("phone_number_id").GetString();

                                if (!string.IsNullOrEmpty(fromPhone) && !string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(phoneNumberId))
                                {
                                    _logger.LogInformation("Received Meta message from {From}: {Text}", fromPhone, text);
                                    await OrchestrateAndResponseAsync(fromPhone, text, phoneNumberId);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background error processing Meta message.");
        }
    }

    private async Task OrchestrateAndResponseAsync(string fromPhone, string text, string phoneNumberId)
    {
        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IChatOrchestrator>();
        var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
        var messageFactory = scope.ServiceProvider.GetRequiredService<IMessageSenderFactory>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        // 1. Resolve Tenant
        // Simple mapping: for now we use 'tenant_1' as fallback or try to find a tenant with this Meta PhoneId
        // In a real scenario, you'd have a mapping table: MetaPhoneId -> TenantId
        string tenantId = "tenant_1"; 
        
        var tenant = await tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
        {
            _logger.LogError("Could not resolve tenant for Meta PhoneId {PhoneId}", phoneNumberId);
            return;
        }

        tenantContext.CurrentTenant = tenant;

        // 2. Orchestrate AI Response
        var sessionId = GenerateSessionId(tenantId, fromPhone);
        var metadata = new Dictionary<string, string> { { "phone", fromPhone } };

        var result = await orchestrator.ProcessMessageAsync(text, sessionId, tenantId, "Meta", metadata);

        // 3. Send Response Back
        var sender = messageFactory.CreateSender(tenant);
        await sender.SendAsync(fromPhone, result.Response);
        
        _logger.LogInformation("Meta response sent to {To}: {Response}", fromPhone, result.Response);
    }

    private static Guid GenerateSessionId(string tenantId, string phone)
    {
        var input = $"{tenantId}:{phone}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
