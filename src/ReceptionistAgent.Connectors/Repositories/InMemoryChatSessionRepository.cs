using System.Collections.Concurrent;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ReceptionistAgent.Connectors.Repositories;

public class InMemoryChatSessionRepository : IChatSessionRepository
{
    private readonly ConcurrentDictionary<Guid, ChatHistory> _sessions = new();
    private readonly ConcurrentDictionary<Guid, bool> _flags = new();
    private readonly ConcurrentDictionary<Guid, string> _phones = new();

    public Task<ChatHistory> GetChatHistoryAsync(Guid sessionId, string tenantId, string systemPrompt, string? userPhone = null)
    {
        if (userPhone != null)
        {
            _phones.TryAdd(sessionId, userPhone);
        }

        if (_sessions.TryGetValue(sessionId, out var history))
        {
            return Task.FromResult(history);
        }

        var newHistory = new ChatHistory(systemPrompt);
        _sessions.TryAdd(sessionId, newHistory);
        return Task.FromResult(newHistory);
    }

    public Task UpdateChatHistoryAsync(Guid sessionId, string tenantId, ChatHistory history)
    {
        _sessions.AddOrUpdate(sessionId, history, (key, oldValue) => history);

        // Ensure flag exists
        _flags.TryAdd(sessionId, false);
        return Task.CompletedTask;
    }

    public Task SetNeedsHumanAttentionAsync(Guid sessionId, string tenantId, bool needsAttention)
    {
        _flags.AddOrUpdate(sessionId, needsAttention, (key, oldValue) => needsAttention);
        return Task.CompletedTask;
    }

    public Task<List<ReceptionistAgent.Core.Models.ChatSessionDto>> GetActiveSessionsAsync(string tenantId)
    {
        var active = _sessions.Select(kvp => new ReceptionistAgent.Core.Models.ChatSessionDto
        {
            Id = kvp.Key,
            TenantId = tenantId,
            UserPhone = _phones.TryGetValue(kvp.Key, out var p) ? p : "Unknown",
            NeedsHumanAttention = _flags.TryGetValue(kvp.Key, out var val) && val,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        return Task.FromResult(active);
    }
}
