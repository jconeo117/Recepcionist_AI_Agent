using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using Xunit;

namespace ReceptionistAgent.Tests.Adapters;

public class SqlClientDataAdapterTests
{
    private const string TestConnectionString = "Server=(localdb)\\mssqllocaldb;Database=ReceptionistAgentTestDb;Trusted_Connection=True;";

    [Fact(Skip = "Requiere LocalDB o SQL Server corriendo para integrarse adecuadamente. Quitar Skip para probar localmente.")]
    public async Task CreateBookingAsync_ShouldInsertRecord()
    {
        // Arrange
        var providers = new List<ServiceProvider>
        {
            new() { Id = "DR001", Name = "Dr. Test" }
        };
        var adapter = new SqlClientDataAdapter(TestConnectionString, providers);
        var booking = new BookingRecord
        {
            TenantId = "TEST-TENANT",
            ClientName = "Juan Pérez",
            ProviderId = "DR001",
            ProviderName = "Dr. Test",
            ScheduledDate = DateTime.UtcNow.Date.AddDays(1),
            ScheduledTime = new TimeSpan(10, 0, 0),
            Status = BookingStatus.Scheduled
        };

        // Act
        var created = await adapter.CreateBookingAsync(booking);

        // Assert
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.StartsWith("CITA-", created.ConfirmationCode);

        // Clean up (assuming tests drop/delete records)
        await adapter.DeleteBookingAsync(created.Id.ToString());
    }
}
