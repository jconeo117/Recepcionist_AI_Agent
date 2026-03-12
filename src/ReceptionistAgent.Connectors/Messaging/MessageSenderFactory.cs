using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.Connectors.Messaging;

public class MessageSenderFactory : IMessageSenderFactory
{
    private readonly ITenantResolver _tenantResolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public MessageSenderFactory(
        ITenantResolver tenantResolver,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _tenantResolver = tenantResolver;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<IMessageSender> CreateSenderAsync(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            throw new ArgumentException($"Tenant {tenantId} not found");

        return CreateSender(tenant);
    }

    public IMessageSender CreateSender(TenantConfiguration tenantConfig)
    {
        var provider = tenantConfig.MessageProvider?.ToLowerInvariant();

        if (provider == "meta")
        {
            var logger = _loggerFactory.CreateLogger<MetaWhatsAppSender>();
            var httpClient = _httpClientFactory.CreateClient("MetaGraphApi");

            var account = !string.IsNullOrWhiteSpace(tenantConfig.MessageProviderAccount)
                ? tenantConfig.MessageProviderAccount
                : _configuration["Meta:PhoneNumberId"] ?? string.Empty;

            var token = !string.IsNullOrWhiteSpace(tenantConfig.MessageProviderToken)
                ? tenantConfig.MessageProviderToken
                : _configuration["Meta:AccessToken"] ?? string.Empty;

            return new MetaWhatsAppSender(httpClient, account, token, logger);
        }
        else // Twilio (Default)
        {
            var logger = _loggerFactory.CreateLogger<TwilioMessageSender>();

            var account = !string.IsNullOrWhiteSpace(tenantConfig.MessageProviderAccount)
                ? tenantConfig.MessageProviderAccount
                : _configuration["Twilio:AccountSid"] ?? string.Empty;

            var token = !string.IsNullOrWhiteSpace(tenantConfig.MessageProviderToken)
                ? tenantConfig.MessageProviderToken
                : _configuration["Twilio:AuthToken"] ?? string.Empty;

            var phone = !string.IsNullOrWhiteSpace(tenantConfig.MessageProviderPhone)
                ? tenantConfig.MessageProviderPhone
                : _configuration["Twilio:FromNumber"] ?? string.Empty;

            return new TwilioMessageSender(account, token, phone, logger);
        }
    }
}
