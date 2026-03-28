using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Api.Models.Requests;
using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Core.Session;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class BookingController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;
    private readonly ClientDataAdapterFactory _adapterFactory;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IMessageSenderFactory _messageSenderFactory;
    private readonly IReminderService _reminderService;
    private readonly ILogger<BookingController> _logger;

    public BookingController(
        ITenantResolver tenantResolver,
        ClientDataAdapterFactory adapterFactory,
        IChatSessionRepository sessionRepository,
        IMessageSenderFactory messageSenderFactory,
        IReminderService reminderService,
        ILogger<BookingController> logger)
    {
        _tenantResolver = tenantResolver;
        _adapterFactory = adapterFactory;
        _sessionRepository = sessionRepository;
        _messageSenderFactory = messageSenderFactory;
        _reminderService = reminderService;
        _logger = logger;
    }

    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings()
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var bookings = await adapter.GetAllBookingsAsync();
        return Ok(bookings.OrderByDescending(b => b.ScheduledDate).ThenByDescending(b => b.ScheduledTime));
    }

    [HttpGet("clients/{phone}")]
    public async Task<IActionResult> GetClientProfile(string phone)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var allBookings = await adapter.GetAllBookingsAsync();
        
        // Find bookings matching phone 
        var clientBookings = allBookings
            .Where(b => (b.ClientName != null && b.ClientName.Contains(phone)) ||
                        (b.CustomFields.TryGetValue("clientId", out var cId) && cId?.ToString() == phone))
            .OrderByDescending(b => b.ScheduledDate)
            .ToList();

        var historySessions = await _sessionRepository.GetSessionsByPhoneAsync(tenantId, phone);

        return Ok(new
        {
            Phone = phone,
            Bookings = clientBookings,
            Sessions = historySessions
        });
    }

    [HttpPut("bookings/{id}/reschedule")]
    public async Task<IActionResult> RescheduleBooking(Guid id, [FromBody] RescheduleRequest request)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var bookings = await adapter.GetAllBookingsAsync();
        var booking = bookings.FirstOrDefault(b => b.Id == id);

        if (booking == null) return NotFound("Booking not found");

        var timeSlot = TimeSpan.Parse(request.Time);
        bool exists = await adapter.ExistsAsync(request.Date, timeSlot, request.ProviderId);
        
        // Only block if it's a DIFFERENT booking taking the slot
        if (exists && (booking.ScheduledDate.Date != request.Date.Date || booking.ScheduledTime != timeSlot || booking.ProviderId != request.ProviderId))
        {
            var conflictingBooking = bookings.FirstOrDefault(b => b.ScheduledDate.Date == request.Date.Date && b.ScheduledTime == timeSlot && b.ProviderId == request.ProviderId && b.Id != booking.Id && b.Status != BookingStatus.Cancelled);
            if (conflictingBooking != null)
                return BadRequest(new { message = "El horario seleccionado ya está ocupado." });
        }

        booking.ScheduledDate = request.Date;
        booking.ScheduledTime = timeSlot;
        booking.ProviderId = request.ProviderId;
        booking.UpdatedAt = DateTime.UtcNow;

        var success = await adapter.UpdateBookingAsync(booking);
        if (!success) return StatusCode(500, "Error updating booking in database.");

        string? phone = null;
        if (booking.CustomFields.TryGetValue("clientId", out var clientIdObj))
            phone = clientIdObj?.ToString();
        else if (!string.IsNullOrWhiteSpace(booking.ClientName) && booking.ClientName.StartsWith("+"))
            phone = booking.ClientName; 

        // Sincronizar recordatorios
        if (!string.IsNullOrEmpty(phone))
        {
            await _reminderService.CancelRemindersForBookingAsync(booking.Id);
            // Re-agendar usando el timezone del tenant (Fix 9)
            var tenantTimezone = tenant.TimeZoneId ?? "America/Bogota";
            await _reminderService.ScheduleRemindersForBookingAsync(booking, phone, "", tenantTimezone);
        }

        if (!string.IsNullOrEmpty(phone))
        {
            var sessions = await _sessionRepository.GetActiveSessionsAsync(tenantId);
            var activeSession = sessions.FirstOrDefault(s => s.UserPhone == phone);
            
            if (activeSession != null)
            {
                var history = await _sessionRepository.GetChatHistoryAsync(activeSession.Id, tenantId, "");
                history.AddMessage(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System, $"El operador humano reprogramó la cita del usuario (Código {booking.ConfirmationCode}) para el {request.Date:yyyy-MM-dd} a las {request.Time}. Informa al usuario sobre esto la próxima vez que te escriba o si pregunta.");
                await _sessionRepository.UpdateChatHistoryAsync(activeSession.Id, tenantId, history);

                // Option to proactively notify via WhatsApp
                try {
                    var sender = await _messageSenderFactory.CreateSenderAsync(tenantId);
                    await sender.SendAsync(phone, $"Hola {booking.ClientName}, te informamos desde la administración que tu cita ha sido ajustada para el *{request.Date:dd/MM/yyyy} a las {request.Time}*. Si tienes dudas, contesta este mensaje.");
                } catch (Exception ex) { 
                    _logger.LogWarning(ex, "Error enviando notificación WhatsApp al cliente {Phone} en tenant {TenantId}. Cita {BookingId} reprogramada.", phone, tenantId, booking.Id);
                } 
            }
        }

        return Ok(booking);
    }
}
