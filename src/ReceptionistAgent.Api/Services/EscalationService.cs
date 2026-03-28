using ReceptionistAgent.AI.Services;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Session;
using System;
using System.Threading.Tasks;

namespace ReceptionistAgent.Api.Services;

public class EscalationService : IEscalationService
{
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IClientDataAdapter _adapter;

    public EscalationService(IChatSessionRepository chatSessionRepository, IClientDataAdapter adapter)
    {
        _chatSessionRepository = chatSessionRepository;
        _adapter = adapter;
    }

    public async Task EscalateSessionAsync(Guid sessionId, string tenantId, string reason)
    {
        // Here we could also log the 'reason', send an email to the clinic, or push an alert.
        // For now, we simply flag the session in the database so it appears in the Client Dashboard Inbox.
        await _chatSessionRepository.SetNeedsHumanAttentionAsync(sessionId, tenantId, true);

        // --- OUTBOX EVENT FOR WEBHOOKS ---
        await _adapter.AddOutboxEventAsync(new OutboxEvent
        {
            TenantId = tenantId,
            EventType = "session.escalated",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { sessionId, reason, timestamp = DateTime.UtcNow }),
            CreatedAt = DateTime.UtcNow
        });
    }
}
