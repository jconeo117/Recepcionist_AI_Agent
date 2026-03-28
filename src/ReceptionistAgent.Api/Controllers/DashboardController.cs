using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel.ChatCompletion;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Connectors.Adapters;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Dapper;
using ReceptionistAgent.Core.Repositories;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize] // Requires JWT
public class DashboardController : ControllerBase
{
    private readonly IChatSessionRepository _sessionRepository;
    private readonly ITenantResolver _tenantResolver;
    private readonly ClientDataAdapterFactory _adapterFactory;
    private readonly IDatabaseAdminRepository _dbAdminRepository;

    public DashboardController(
        IChatSessionRepository sessionRepository,
        ITenantResolver tenantResolver,
        ClientDataAdapterFactory adapterFactory,
        IDatabaseAdminRepository dbAdminRepository)
    {
        _sessionRepository = sessionRepository;
        _tenantResolver = tenantResolver;
        _adapterFactory = adapterFactory;
        _dbAdminRepository = dbAdminRepository;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        var bookingStats = await adapter.GetBookingStatsAsync();
        var providerCount = await adapter.GetProviderCountAsync();
        var activeSessions = await _sessionRepository.GetActiveSessionsAsync(tenantId);

        return Ok(new
        {
            TotalBookings = bookingStats.TotalBookings,
            PendingBookings = bookingStats.ScheduledCount,
            ProviderCount = providerCount,
            ActiveSessions = activeSessions.Count,
            NeedsAttention = activeSessions.Count(s => s.NeedsHumanAttention)
        });
    }

    [HttpGet("database/{tableName}")]
    public async Task<IActionResult> GetDatabaseTable(string tableName)
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        var adapter = _adapterFactory.CreateAdapter(tenant);
        
        if (tableName.Equals("bookings", StringComparison.OrdinalIgnoreCase))
        {
            var data = await adapter.GetAllBookingsAsync();
            return Ok(data.OrderByDescending(b => b.CreatedAt).Take(50));
        }
        
        if (tableName.Equals("providers", StringComparison.OrdinalIgnoreCase))
        {
            var data = await adapter.GetAllProvidersAsync();
            return Ok(data);
        }

        if (tableName.Equals("clients", StringComparison.OrdinalIgnoreCase))
        {
            var bookings = await adapter.GetAllBookingsAsync();
            var clients = bookings
                .Where(b => b.ClientName != null)
                .GroupBy(b => b.ClientName)
                .Select(g => new { 
                    Name = g.Key, 
                    LastBooking = g.Max(b => b.ScheduledDate),
                    TotalBookings = g.Count()
                })
                .OrderByDescending(c => c.LastBooking)
                .Take(50);
            return Ok(clients);
        }

        if (tableName.Equals("messages", StringComparison.OrdinalIgnoreCase))
        {
            // Inbox compatible sessions view
            var sessions = await _sessionRepository.GetActiveSessionsAsync(tenantId);
            return Ok(sessions.Select(s => new {
                s.Id,
                s.UserPhone,
                s.UpdatedAt,
                s.NeedsHumanAttention
            }).OrderByDescending(s => s.UpdatedAt).Take(50));
        }

        if (tableName.Equals("chat_messages", StringComparison.OrdinalIgnoreCase))
        {
            // Raw relational messages table using Admin Repos (Clean Architecture)
            var messages = await _dbAdminRepository.GetRecentChatMessagesAsync(tenant, 50);
            return Ok(messages);
        }

        if (tableName.Equals("services", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(tenant.Services.Select(s => new { Service = s }));
        }

        return BadRequest(new { error = $"La tabla '{tableName}' no está disponible para visualización dinámica o aún no tiene una implementación de mapeo." });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var tenantId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(tenantId)) return Unauthorized();

        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null) return NotFound();

        return Ok(new {
            tenant.TenantId,
            tenant.BusinessName,
            tenant.BusinessType,
            tenant.TimeZoneId,
            tenant.Address,
            tenant.Phone,
            tenant.WorkingHours,
            ServiceModality = tenant.ServiceModality.ToString(),
            BookingRequirements = new {
                tenant.BookingRequirements.RequiresEmail,
                tenant.BookingRequirements.RequiresBirthDate,
                tenant.BookingRequirements.RequiresInsurance
            }
        });
    }
}
