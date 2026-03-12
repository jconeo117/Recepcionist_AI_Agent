using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel.ChatCompletion;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Core.Services;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize] // Requires JWT
public class DashboardController : ControllerBase
{
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IMessageSenderFactory _messageSenderFactory;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<ReceptionistAgent.Api.Hubs.DashboardHub> _hubContext;

    public DashboardController(
        IChatSessionRepository sessionRepository,
        IMessageSenderFactory messageSenderFactory,
        Microsoft.AspNetCore.SignalR.IHubContext<ReceptionistAgent.Api.Hubs.DashboardHub> hubContext)
    {
        _sessionRepository = sessionRepository;
        _messageSenderFactory = messageSenderFactory;
        _hubContext = hubContext;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var sessions = await _sessionRepository.GetActiveSessionsAsync(tenantId);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId}/history")]
    public async Task<IActionResult> GetSessionHistory(Guid sessionId)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        // Pass empty string for systemPrompt to retrieve existing history
        var history = await _sessionRepository.GetChatHistoryAsync(sessionId, tenantId, "");
        var formattedHistory = history.Select(m => new { Role = m.Role.ToString(), Content = m.Content });

        return Ok(formattedHistory);
    }

    [HttpPost("sessions/{sessionId}/reply")]
    public async Task<IActionResult> ReplyToSession(Guid sessionId, [FromBody] ReplyRequest request)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var activeSessions = await _sessionRepository.GetActiveSessionsAsync(tenantId);
        var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);

        if (session == null || string.IsNullOrEmpty(session.UserPhone))
            return NotFound("Session or UserPhone not found.");

        // Read history and append human reply so the AI knows what was said
        var history = await _sessionRepository.GetChatHistoryAsync(sessionId, tenantId, "");
        history.AddMessage(AuthorRole.Assistant, $"[Agente Humano]: {request.Message}");
        await _sessionRepository.UpdateChatHistoryAsync(sessionId, tenantId, history);

        // Clear NeedsHumanAttention flag since a human replied
        await _sessionRepository.SetNeedsHumanAttentionAsync(sessionId, tenantId, false);

        // Send the message via WhatsApp/Twilio
        var sender = await _messageSenderFactory.CreateSenderAsync(tenantId);
        await sender.SendAsync(session.UserPhone, request.Message);

        // Broadcast real-time update to the Client Dashboard via SignalR WebSockets
        if (_hubContext != null)
        {
            await _hubContext.Clients.Group(tenantId).SendAsync("ReceiveSessionUpdate");
        }

        return Ok(new { success = true });
    }

    public class ReplyRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}
