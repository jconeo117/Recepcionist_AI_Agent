using Microsoft.Extensions.Logging;
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

    public MessageSenderFactory(
        ITenantResolver tenantResolver,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory)
    {
        _tenantResolver = tenantResolver;
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
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
            return new MetaWhatsAppSender(
                httpClient,
                tenantConfig.MessageProviderAccount,
                tenantConfig.MessageProviderToken,
                logger);
        }
        else // Twilio (Default)
        {
            var logger = _loggerFactory.CreateLogger<TwilioMessageSender>();
            return new TwilioMessageSender(
                tenantConfig.MessageProviderAccount,
                tenantConfig.MessageProviderToken,
                tenantConfig.MessageProviderPhone,
                logger);
        }
    }
}
