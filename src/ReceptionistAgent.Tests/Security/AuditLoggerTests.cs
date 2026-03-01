using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Connectors.Security;
using Xunit;

namespace ReceptionistAgent.Tests.Security;

public class AuditLoggerTests
{
    private readonly InMemoryAuditLogger _logger = new();

    [Fact]
    public async Task LogAsync_ShouldStoreEntry()
    {
        var entry = new AuditEntry
        {
            TenantId = "test-tenant",
            SessionId = Guid.NewGuid(),
            EventType = "UserMessage",
            Content = "Quiero una cita"
        };

        await _logger.LogAsync(entry);

        var all = await _logger.GetAllEventsAsync(null);
        Assert.Single(all);
        Assert.Equal("Quiero una cita", all.First().Content);
    }

    [Fact]
    public async Task GetSessionAuditAsync_ShouldFilterBySession()
    {
        var sessionId = Guid.NewGuid();
        var otherSession = Guid.NewGuid();

        await _logger.LogAsync(new AuditEntry { SessionId = sessionId, EventType = "UserMessage", Content = "msg1" });
        await _logger.LogAsync(new AuditEntry { SessionId = otherSession, EventType = "UserMessage", Content = "msg2" });
        await _logger.LogAsync(new AuditEntry { SessionId = sessionId, EventType = "AgentResponse", Content = "resp1" });

        var results = await _logger.GetSessionAuditAsync(sessionId);

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.Equal(sessionId, e.SessionId));
    }

    [Fact]
    public async Task GetSecurityEventsAsync_ShouldFilterByTypeAndTenant()
    {
        await _logger.LogAsync(new AuditEntry
        {
            TenantId = "clinic-a",
            EventType = "SecurityBlock",
            Content = "Blocked",
            ThreatLevel = ThreatLevel.High
        });

        await _logger.LogAsync(new AuditEntry
        {
            TenantId = "clinic-b",
            EventType = "SecurityBlock",
            Content = "Blocked other"
        });

        await _logger.LogAsync(new AuditEntry
        {
            TenantId = "clinic-a",
            EventType = "UserMessage",
            Content = "Normal"
        });

        var results = await _logger.GetSecurityEventsAsync("clinic-a", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        Assert.Single(results);
        Assert.Equal("Blocked", results.First().Content);
    }

    [Fact]
    public async Task GetAllEventsAsync_ShouldRespectLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            await _logger.LogAsync(new AuditEntry
            {
                TenantId = "test",
                EventType = "UserMessage",
                Content = $"msg-{i}"
            });
        }

        var results = await _logger.GetAllEventsAsync(null, limit: 5);

        Assert.Equal(5, results.Count);
    }
}
