using System.Net.Http;
using Microsoft.Extensions.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ReceptionistAgent.Core.Services;

public interface IWebhookSenderService
{
    Task SendEventAsync(string url, string eventType, object payload);
}

public class WebhookSenderService : IWebhookSenderService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookSenderService> _logger;

    public WebhookSenderService(IHttpClientFactory httpClientFactory, ILogger<WebhookSenderService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendEventAsync(string url, string eventType, object payload)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var client = _httpClientFactory.CreateClient("WebhookClient");
            var json = JsonSerializer.Serialize(new
            {
                @event = eventType,
                timestamp = DateTime.UtcNow,
                data = payload
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook delivery failed for {Url}. Status: {Status}", url, response.StatusCode);
            }
            else
            {
                _logger.LogInformation("Webhook delivered successfully to {Url} for event {Event}", url, eventType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending webhook to {Url}", url);
        }
    }
}
