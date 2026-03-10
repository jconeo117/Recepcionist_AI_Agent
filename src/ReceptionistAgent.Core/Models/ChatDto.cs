using System;

namespace ReceptionistAgent.Core.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
}

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
}

public class ChatSessionDto
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserPhone { get; set; } = string.Empty;
    public bool NeedsHumanAttention { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
