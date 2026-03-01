using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using Xunit;

namespace ReceptionistAgent.Tests.Adapters;

public class ClientDataAdapterFactoryTests
{
    private readonly ClientDataAdapterFactory _factory;

    public ClientDataAdapterFactoryTests()
    {
        _factory = new ClientDataAdapterFactory();
    }

    [Fact]
    public void CreateAdapter_WithSqlServerDbType_ShouldReturnSqlClientDataAdapter()
    {
        // Arrange
        var tenantConfig = new TenantConfiguration
        {
            TenantId = "TEST-TENANT",
            DbType = "SqlServer",
            ConnectionString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;",
            Providers = new List<TenantProviderConfig>
            {
                new() { Id = "P1", Name = "Provider 1" }
            }
        };

        // Act
        var adapter = _factory.CreateAdapter(tenantConfig);

        // Assert
        Assert.IsType<SqlClientDataAdapter>(adapter);
    }

    [Fact]
    public void CreateAdapter_WithSqlServerDbType_WithoutConnectionString_ShouldThrow()
    {
        // Arrange
        var tenantConfig = new TenantConfiguration
        {
            TenantId = "TEST-TENANT",
            DbType = "SqlServer",
            ConnectionString = string.Empty, // Missing connection string
            Providers = new List<TenantProviderConfig>
            {
                new() { Id = "P1", Name = "Provider 1" }
            }
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _factory.CreateAdapter(tenantConfig));
        Assert.Contains("no ConnectionString was found", ex.Message);
    }

    [Fact]
    public async Task CreateAdapter_ShouldReturnAdapterWithTenantProviders()
    {
        var config = new TenantConfiguration
        {
            TenantId = "test",
            Providers =
            [
                new TenantProviderConfig
                {
                    Id = "P1",
                    Name = "Dr. Test",
                    Role = "General",
                    WorkingDays = ["Monday", "Wednesday"],
                    StartTime = "09:00",
                    EndTime = "17:00",
                    SlotDurationMinutes = 30
                }
            ]
        };

        var adapter = _factory.CreateAdapter(config);
        var providers = await adapter.GetAllProvidersAsync();

        Assert.Single(providers);
        Assert.Equal("Dr. Test", providers.First().Name);
        Assert.Equal("test", providers.First().TenantId);
        Assert.Contains(DayOfWeek.Monday, providers.First().WorkingDays);
        Assert.Contains(DayOfWeek.Wednesday, providers.First().WorkingDays);
        Assert.Equal(new TimeSpan(9, 0, 0), providers.First().StartTime);
    }

    [Fact]
    public async Task CreateAdapter_ShouldReturnEmptyAdapter_WhenNoProviders()
    {
        var config = new TenantConfiguration
        {
            TenantId = "empty",
            Providers = []
        };

        var adapter = _factory.CreateAdapter(config);
        var providers = await adapter.GetAllProvidersAsync();

        Assert.Empty(providers);
    }

    [Fact]
    public async Task CreateAdapter_ShouldIsolateDataBetweenTenants()
    {
        var config1 = new TenantConfiguration
        {
            TenantId = "tenant-1",
            Providers =
            [
                new TenantProviderConfig { Id = "P1", Name = "Provider 1", Role = "Role A", WorkingDays = ["Monday"] }
            ]
        };
        var config2 = new TenantConfiguration
        {
            TenantId = "tenant-2",
            Providers =
            [
                new TenantProviderConfig { Id = "P2", Name = "Provider 2", Role = "Role B", WorkingDays = ["Tuesday"] }
            ]
        };

        var adapter1 = _factory.CreateAdapter(config1);
        var adapter2 = _factory.CreateAdapter(config2);

        // Crear booking en tenant 1
        await adapter1.CreateBookingAsync(new BookingRecord
        {
            ClientName = "Client 1",
            ProviderId = "P1",
            ScheduledDate = DateTime.UtcNow.Date,
            ScheduledTime = new TimeSpan(10, 0, 0)
        });

        // Verificar aislamiento
        var bookingsTenant1 = await adapter1.GetAllBookingsAsync();
        var bookingsTenant2 = await adapter2.GetAllBookingsAsync();

        Assert.Single(bookingsTenant1);
        Assert.Empty(bookingsTenant2);
    }
}
