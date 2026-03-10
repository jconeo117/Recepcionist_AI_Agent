using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Core.Services;

namespace ReceptionistAgent.Connectors.Messaging;

/// <summary>
/// Envía mensajes outbound por la Meta Cloud API oficial de WhatsApp Business.
/// Requiere estar configurado en TenantConfiguration con ProviderType = "Meta".
/// </summary>
public class MetaWhatsAppSender : IMessageSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;

    public MetaWhatsAppSender(HttpClient httpClient, string phoneNumberId, string accessToken, ILogger logger)
    {
        _httpClient = httpClient;
        _phoneNumberId = phoneNumberId;
        _accessToken = accessToken;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string message)
    {
        try
        {
            // Clean phone number: remove 'whatsapp:' prefix if exists and '+'
            var cleanPhone = to.Replace("whatsapp:", "").Replace("+", "");

            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = cleanPhone,
                type = "text",
                text = new { body = message }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/v19.0/{_phoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Meta message sent to {To}. Response: {Response}", to, responseBody);
                return true;
            }
            else
            {
                _logger.LogError("Failed to send Meta message to {To}. Status: {Status}, Body: {Body}",
                    to, response.StatusCode, responseBody);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending Meta message to {To}", to);
            return false;
        }
    }
}
