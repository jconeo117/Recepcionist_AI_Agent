using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Security;
using Moq;
using Xunit;

namespace ReceptionistAgent.Tests.Services;

public class BookingServiceTests
{
    private readonly Mock<IClientDataAdapter> _mockAdapter;
    private readonly Mock<IAuditLogger> _mockAuditLogger;
    private readonly BookingService _service;
    private readonly List<ServiceProvider> _testProviders;

    public BookingServiceTests()
    {
        _mockAdapter = new Mock<IClientDataAdapter>();

        _testProviders =
        [
            new ServiceProvider
            {
                Id = "PRV001",
                Name = "Dr. Carlos Ramírez",
                Role = "Oftalmología General",
                WorkingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(18, 0, 0),
                SlotDurationMinutes = 30
            },
            new ServiceProvider
            {
                Id = "PRV002",
                Name = "Dra. María González",
                Role = "Retina",
                WorkingDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(16, 0, 0),
                SlotDurationMinutes = 30
            }
        ];

        _mockAdapter.Setup(a => a.GetAllProvidersAsync())
            .ReturnsAsync(_testProviders);

        _mockAuditLogger = new Mock<IAuditLogger>();
        _service = new BookingService(_mockAdapter.Object, new TenantContext(), _mockAuditLogger.Object);
    }

    // === GetAvailableSlotsAsync ===

    [Fact]
    public async Task GetAvailableSlotsAsync_ShouldReturnSlots_WhenProviderWorksOnDate()
    {
        // Arrange
        var providerId = "PRV001";
        var date = new DateTime(2026, 2, 23); // Monday

        _mockAdapter.Setup(a => a.ExistsAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _service.GetAvailableSlotsAsync(providerId, date);

        // Assert
        Assert.NotEmpty(result);
        Assert.All(result, slot => Assert.True(slot.IsAvailable));
        Assert.Equal(date, result.First().Date);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ShouldReturnEmpty_WhenProviderDoesNotWorkOnDate()
    {
        // Arrange
        var providerId = "PRV002"; // Mon, Wed, Fri
        var date = new DateTime(2026, 2, 24); // Tuesday

        // Act
        var result = await _service.GetAvailableSlotsAsync(providerId, date);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ShouldMarkSlotAsUnavailable_WhenBooked()
    {
        // Arrange
        var providerId = "PRV001";
        var date = new DateTime(2026, 2, 23); // Monday
        var bookedTime = new TimeSpan(9, 0, 0);

        _mockAdapter.Setup(a => a.ExistsAsync(date, bookedTime, providerId))
            .ReturnsAsync(true);

        // Act
        var result = await _service.GetAvailableSlotsAsync(providerId, date);

        // Assert
        var bookedSlot = result.First(s => s.Time == bookedTime);
        Assert.False(bookedSlot.IsAvailable);
    }

    // === CreateBookingAsync ===

    [Fact]
    public async Task CreateBookingAsync_ShouldCreateBooking_WhenSlotIsAvailable()
    {
        // Arrange
        var providerId = "PRV001";
        var date = new DateTime(2026, 2, 23); // Monday
        var time = new TimeSpan(10, 0, 0);

        _mockAdapter.Setup(a => a.ExistsAsync(date, time, providerId))
            .ReturnsAsync(false);

        _mockAdapter.Setup(a => a.CreateBookingAsync(It.IsAny<BookingRecord>()))
            .ReturnsAsync((BookingRecord b) => b);

        var customFields = new Dictionary<string, object>
        {
            ["phone"] = "123456789",
            ["email"] = "john@test.com",
            ["reason"] = "Checkup"
        };

        // Act
        var result = await _service.CreateBookingAsync("John Doe", providerId, date, time, customFields);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(date, result.ScheduledDate);
        Assert.Equal(time, result.ScheduledTime);
        Assert.Equal(BookingStatus.Confirmed, result.Status);
        Assert.Equal("123456789", result.CustomFields["phone"].ToString());
    }

    [Fact]
    public async Task CreateBookingAsync_ShouldThrow_WhenSlotIsOccupied()
    {
        // Arrange
        var providerId = "PRV001";
        var date = new DateTime(2026, 2, 23);
        var time = new TimeSpan(10, 0, 0);

        _mockAdapter.Setup(a => a.ExistsAsync(date, time, providerId))
            .ReturnsAsync(true);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateBookingAsync("John Doe", providerId, date, time));
    }

    [Fact]
    public async Task CreateBookingAsync_ShouldThrow_WhenProviderDoesNotWorkOnDate()
    {
        // Arrange
        var providerId = "PRV002"; // Mon, Wed, Fri
        var date = new DateTime(2026, 2, 24); // Tuesday
        var time = new TimeSpan(10, 0, 0);

        _mockAdapter.Setup(a => a.ExistsAsync(date, time, providerId))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateBookingAsync("John Doe", providerId, date, time));
    }
}
