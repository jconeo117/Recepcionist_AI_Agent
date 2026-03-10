namespace ReceptionistAgent.AI.Services;

public interface IEscalationService
{
    Task EscalateSessionAsync(Guid sessionId, string tenantId, string reason);
}
