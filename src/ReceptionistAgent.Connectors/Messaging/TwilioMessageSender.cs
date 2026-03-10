using Microsoft.Extensions.Logging;
using ReceptionistAgent.Core.Services;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ReceptionistAgent.Connectors.Messaging;

/// <summary>
/// Envía mensajes outbound por Twilio API (WhatsApp).
/// Actualizado para soportar multi-tenant de forma aislada.
/// </summary>
public class TwilioMessageSender : IMessageSender
{
    private readonly ILogger _logger;
    private readonly string _fromNumber;
    private readonly TwilioRestClient _twilioClient;

    public TwilioMessageSender(string accountSid, string authToken, string fromNumber, ILogger logger)
    {
        _logger = logger;
        _fromNumber = fromNumber;
        _twilioClient = new TwilioRestClient(accountSid, authToken);
    }

    public async Task<bool> SendAsync(string to, string message)
    {
        try
        {
            var messageResource = await MessageResource.CreateAsync(
                body: message,
                from: new PhoneNumber(_fromNumber),
                to: new PhoneNumber(to.StartsWith("whatsapp:") ? to : $"whatsapp:{to}"),
                client: _twilioClient
            );

            _logger.LogInformation(
                "Twilio message sent: SID={Sid}, To={To}, Status={Status}",
                messageResource.Sid, to, messageResource.Status);

            return messageResource.Status != MessageResource.StatusEnum.Failed &&
                   messageResource.Status != MessageResource.StatusEnum.Undelivered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Twilio message to {To}", to);
            return false;
        }
    }
}
