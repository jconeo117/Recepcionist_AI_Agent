using Microsoft.SemanticKernel.ChatCompletion;
using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Session;

public interface IChatSessionRepository
{
    Task<ChatHistory> GetChatHistoryAsync(Guid sessionId, string tenantId, string systemPrompt, string? userPhone = null);
    Task UpdateChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history, string? userPhone = null);

    // Feature: Human Escalation
    Task SetNeedsHumanAttentionAsync(Guid sessionId, string tenantId, bool needsAttention);
    Task<List<ChatSessionDto>> GetActiveSessionsAsync(string tenantId);
    
    // Feature: Patient CRM Profile
    Task<List<ChatSessionDto>> GetSessionsByPhoneAsync(string tenantId, string phone);
}
