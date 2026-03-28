using System;

namespace ReceptionistAgent.Core.Models;

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // e.g., "BookingCreated", "BookingCancelled"
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
}
