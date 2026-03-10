using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Api.Services;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using System.Text.Json;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/webhook/meta")]
public class MetaWebhookController : ControllerBase
{
    private readonly ChatOrchestrator _orchestrator;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<MetaWebhookController> _logger;
    private readonly string _verifyToken;

    public MetaWebhookController(
        ChatOrchestrator orchestrator,
        ITenantResolver tenantResolver,
        ILogger<MetaWebhookController> logger,
        IConfiguration config)
    {
        _orchestrator = orchestrator;
        _tenantResolver = tenantResolver;
        _logger = logger;
        // The verify token you configure in the Meta App Dashboard
        _verifyToken = config["Meta:VerifyToken"] ?? "my_secure_verify_token_123";
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
        if (mode == "subscribe" && token == _verifyToken)
        {
            _logger.LogInformation("Meta Webhook verified successfully.");
            return Ok(challenge);
        }

        _logger.LogWarning("Meta Webhook verification failed. Token mismatch.");
        return Forbid();
    }

    /// <summary>
    /// Endpoint to receive incoming messages from WhatsApp Cloud API.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveMessage([FromBody] JsonElement body)
    {
        try
        {
            if (body.TryGetProperty("object", out var objProperty) && objProperty.GetString() == "whatsapp_business_account")
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

                                    // For Meta API, the phone number ID is usually what identifies the tenant/business
                                    var phoneNumberId = value.GetProperty("metadata").GetProperty("phone_number_id").GetString();

                                    _logger.LogInformation("Received Meta message from {From}: {Text}", fromPhone, text);

                                    // Resolve tenant by Phone Number ID (configured in mappings or DB)
                                    // Let's assume the TenantResolver can handle finding a tenant by their Meta Phone_Number_Id
                                    // Here we simulate resolving the tenant. You might need to update InMemoryTenantResolver to map this.
                                    var tenantId = await ResolveTenantFromMetaIdAsync(phoneNumberId);

                                    if (tenantId != null && !string.IsNullOrEmpty(fromPhone) && !string.IsNullOrEmpty(text))
                                    {
                                        var sessionId = GenerateSessionId(tenantId, fromPhone);
                                        var metadata = new Dictionary<string, string> { { "phone", fromPhone } };

                                        // Process message in the background
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await _orchestrator.ProcessMessageAsync(text, sessionId, tenantId, "Meta", metadata);
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Error processing Meta message for session {SessionId}", sessionId);
                                            }
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

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

    private async Task<string?> ResolveTenantFromMetaIdAsync(string phoneNumberId)
    {
        // For local development or mock, just return a default tenant if you don't have a mapping table.
        // In a real database, you query: SELECT TenantId FROM TenantConfigurations WHERE MetaPhoneNumberId = @id
        return await Task.FromResult("tenant_1");
    }

    private static Guid GenerateSessionId(string tenantId, string phone)
    {
        var input = $"{tenantId}:{phone}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
