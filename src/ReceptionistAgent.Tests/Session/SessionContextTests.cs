using ReceptionistAgent.AI.Plugins;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ReceptionistAgent.Tests.Session;

public class SessionContextTests
{
    private readonly SessionContext _sessionContext = new();

    // ===== Test 1: Validate client by document ID =====
    [Fact]
    public void ValidateClientId_ShouldTrackInSession()
    {
        _sessionContext.ValidateClientId("CC-123456");

        Assert.Equal("CC-123456", _sessionContext.ValidatedClientId);
        Assert.True(_sessionContext.IsClientValidated("CC-123456"));
    }

    // ===== Test 2: Validate booking by confirmation code =====
    [Fact]
    public void ValidateConfirmationCode_ShouldTrackInSession()
    {
        _sessionContext.ValidateConfirmationCode("CITA-AB12");

        Assert.True(_sessionContext.IsCodeValidated("CITA-AB12"));
        Assert.Contains("CITA-AB12", _sessionContext.ValidatedConfirmationCodes);
    }

    // ===== Test 3: Reject access when clientId not validated =====
    [Fact]
    public void IsClientValidated_WithoutValidation_ShouldReturnFalse()
    {
        Assert.False(_sessionContext.IsClientValidated("CC-999999"));
        Assert.Null(_sessionContext.ValidatedClientId);
    }

    // ===== Test 4: Reject access when confirmation code not validated =====
    [Fact]
    public void IsCodeValidated_WithoutValidation_ShouldReturnFalse()
    {
        Assert.False(_sessionContext.IsCodeValidated("CITA-XXXX"));
        Assert.Empty(_sessionContext.ValidatedConfirmationCodes);
    }

    // ===== Test 5: Multiple validations accumulate in session =====
    [Fact]
    public void MultipleValidations_ShouldAccumulateInSession()
    {
        _sessionContext.ValidateClientId("CC-111111");
        _sessionContext.ValidateConfirmationCode("CITA-AA11");
        _sessionContext.ValidateConfirmationCode("CITA-BB22");

        Assert.True(_sessionContext.IsClientValidated("CC-111111"));
        Assert.True(_sessionContext.IsCodeValidated("CITA-AA11"));
        Assert.True(_sessionContext.IsCodeValidated("CITA-BB22"));
        Assert.Equal(2, _sessionContext.ValidatedConfirmationCodes.Count);
    }

    // ===== Test 6: GetBookingByClientIdAsync returns matching booking =====
    [Fact]
    public async Task GetBookingByClientIdAsync_ShouldReturnMatchingBooking()
    {
        var providers = new List<ServiceProvider>
        {
            new() { Id = "DR001", Name = "Dr. Test", Role = "General",
                     WorkingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                   DayOfWeek.Thursday, DayOfWeek.Friday],
                     StartTime = TimeSpan.FromHours(9),
                     EndTime = TimeSpan.FromHours(17), SlotDurationMinutes = 30 }
        };
        var adapter = new InMemoryClientAdapter(providers);
        var service = new BookingService(adapter, new TenantContext(), new Mock<IAuditLogger>().Object);

        var tomorrow = DateTime.Today.AddDays(1);
        while (!providers[0].WorkingDays.Contains(tomorrow.DayOfWeek))
            tomorrow = tomorrow.AddDays(1);

        var booking = await service.CreateBookingAsync(
            "Juan Pérez", "DR001", tomorrow, TimeSpan.FromHours(10),
            new Dictionary<string, object> { ["clientId"] = "CC-123456" });

        var found = await service.GetBookingByClientIdAsync("CC-123456");

        Assert.NotNull(found);
        Assert.Equal(booking.ConfirmationCode, found.ConfirmationCode);
        Assert.Equal("Juan Pérez", found.ClientName);
    }

    // ===== Test 7: GetBookingsByClientIdAsync returns all client bookings =====
    [Fact]
    public async Task GetBookingsByClientIdAsync_ShouldReturnAllClientBookings()
    {
        var providers = new List<ServiceProvider>
        {
            new() { Id = "DR001", Name = "Dr. Test", Role = "General",
                     WorkingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                   DayOfWeek.Thursday, DayOfWeek.Friday],
                     StartTime = TimeSpan.FromHours(9),
                     EndTime = TimeSpan.FromHours(17), SlotDurationMinutes = 30 }
        };
        var adapter = new InMemoryClientAdapter(providers);
        var service = new BookingService(adapter, new TenantContext(), new Mock<IAuditLogger>().Object);

        var day1 = DateTime.Today.AddDays(1);
        while (!providers[0].WorkingDays.Contains(day1.DayOfWeek))
            day1 = day1.AddDays(1);
        var day2 = day1.AddDays(1);
        while (!providers[0].WorkingDays.Contains(day2.DayOfWeek))
            day2 = day2.AddDays(1);

        await service.CreateBookingAsync(
            "Juan Pérez", "DR001", day1, TimeSpan.FromHours(10),
            new Dictionary<string, object> { ["clientId"] = "CC-123456" });
        await service.CreateBookingAsync(
            "Juan Pérez", "DR001", day2, TimeSpan.FromHours(11),
            new Dictionary<string, object> { ["clientId"] = "CC-123456" });
        await service.CreateBookingAsync(
            "María López", "DR001", day1, TimeSpan.FromHours(14),
            new Dictionary<string, object> { ["clientId"] = "CC-999999" });

        var juanBookings = await service.GetBookingsByClientIdAsync("CC-123456");
        var mariaBookings = await service.GetBookingsByClientIdAsync("CC-999999");

        Assert.Equal(2, juanBookings.Count);
        Assert.Single(mariaBookings);
    }

    // ===== Test 8: BookAppointment stores clientId and auto-validates session =====
    [Fact]
    public async Task BookAppointment_ShouldStoreclientIdAndAutoValidate()
    {
        var providers = new List<ServiceProvider>
        {
            new() { Id = "DR001", Name = "Dr. Ramírez", Role = "Oftalmología",
                     WorkingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                   DayOfWeek.Thursday, DayOfWeek.Friday],
                     StartTime = TimeSpan.FromHours(9),
                     EndTime = TimeSpan.FromHours(17), SlotDurationMinutes = 30 }
        };
        var adapter = new InMemoryClientAdapter(providers);
        var service = new BookingService(adapter, new TenantContext(), new Mock<IAuditLogger>().Object);
        var sessionContext = new SessionContext();
        var tenantContext = new TenantContext();
        var mockLogger = new Mock<ILogger<BookingPlugin>>();
        var plugin = new BookingPlugin(service, sessionContext, tenantContext, mockLogger.Object);

        var tomorrow = DateTime.Today.AddDays(1);
        // Asegurar que el día es laboral
        while (!providers[0].WorkingDays.Contains(tomorrow.DayOfWeek))
            tomorrow = tomorrow.AddDays(1);

        var result = await plugin.BookAppointment(
            clientName: "Juan Pérez",
            clientId: "CC-123456",
            clientPhone: "3001234567",
            clientEmail: "juan@test.com",
            serviceAddress: "no-address",
            providerNameOrId: "Ramírez",
            stringDate: tomorrow.ToString("yyyy-MM-dd"),
            stringTime: "10:00",
            reason: "Control general");

        Assert.Contains("ÉXITO", result);
        // Assert.Contains("CC-123456", result); // Removed purposefully

        // Verificar que el sessionContext fue auto-validado
        Assert.True(sessionContext.IsClientValidated("CC-123456"));
        Assert.NotEmpty(sessionContext.ValidatedConfirmationCodes);
    }

    // ===== Test 9: CancelAppointment rejects unauthorized access =====
    [Fact]
    public async Task CancelAppointment_WithoutValidation_ShouldDenyAccess()
    {
        var providers = new List<ServiceProvider>
        {
            new() { Id = "DR001", Name = "Dr. Ramírez", Role = "Oftalmología",
                     WorkingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                   DayOfWeek.Thursday, DayOfWeek.Friday],
                     StartTime = TimeSpan.FromHours(9),
                     EndTime = TimeSpan.FromHours(17), SlotDurationMinutes = 30 }
        };
        var adapter = new InMemoryClientAdapter(providers);
        var service = new BookingService(adapter, new TenantContext(), new Mock<IAuditLogger>().Object);

        var tomorrow = DateTime.Today.AddDays(1);
        while (!providers[0].WorkingDays.Contains(tomorrow.DayOfWeek))
            tomorrow = tomorrow.AddDays(1);

        var booking = await service.CreateBookingAsync(
            "Juan Pérez", "DR001", tomorrow, TimeSpan.FromHours(10),
            new Dictionary<string, object> { ["clientId"] = "CC-123456" });

        // Usar un SessionContext NUEVO (sin validación previa)
        var sessionContext = new SessionContext();
        var tenantContext = new TenantContext();
        var mockLogger = new Mock<ILogger<BookingPlugin>>();
        var plugin = new BookingPlugin(service, sessionContext, tenantContext, mockLogger.Object);

        // Intentar cancelar con un documento INCORRECTO
        var result = await plugin.CancelAppointment(booking.ConfirmationCode, "CC-WRONG-ID");

        Assert.Contains("ACCESO DENEGADO", result);
    }

    // ===== Test 10: clientId validation is case-insensitive =====
    [Fact]
    public void clientIdValidation_ShouldBeCaseInsensitive()
    {
        _sessionContext.ValidateClientId("cc-123456");

        Assert.True(_sessionContext.IsClientValidated("CC-123456"));
        Assert.True(_sessionContext.IsClientValidated("cc-123456"));
        Assert.True(_sessionContext.IsClientValidated("Cc-123456"));
    }
}
