using Microsoft.SemanticKernel.ChatCompletion;

namespace ReceptionistAgent.Connectors.Repositories;

public interface IChatSessionRepository
{
    Task<ChatHistory> GetChatHistoryAsync(Guid sessionId, string tenantId, string systemPrompt, string? userPhone = null);
    Task UpdateChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone = null);

    // Feature: Human Escalation
    Task SetNeedsHumanAttentionAsync(Guid sessionId, string tenantId, bool needsAttention);
    Task<List<ReceptionistAgent.Core.Models.ChatSessionDto>> GetActiveSessionsAsync(string tenantId);
}
