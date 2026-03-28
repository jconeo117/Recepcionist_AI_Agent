using ReceptionistAgent.Core.Models;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace ReceptionistAgent.Api.Services;

public class OrchestrationResult
{
    public string Response { get; set; } = string.Empty;
    public bool WasFiltered { get; set; }
    public List<string> RedactedItems { get; set; } = new();
}

public interface IChatOrchestrator
{
    Task<OrchestrationResult> ProcessMessageAsync(
        string message,
        Guid sessionId,
        string tenantId,
        string eventTypePrefix,
        Dictionary<string, string>? additionalMetadata = null,
        string? messageId = null);
}
