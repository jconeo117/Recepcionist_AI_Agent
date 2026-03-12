using ReceptionistAgent.AI.Plugins;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using Moq;
using Xunit;

namespace ReceptionistAgent.Tests.Plugins;

public class BusinessInfoPluginTests
{
    private readonly Mock<IClientDataAdapter> _mockAdapter;
    private readonly TenantContext _tenantContext;
    private readonly BusinessInfoPlugin _plugin;

    public BusinessInfoPluginTests()
    {
        _mockAdapter = new Mock<IClientDataAdapter>();
        _tenantContext = new TenantContext
        {
            CurrentTenant = new TenantConfiguration
            {
                TenantId = "test-tenant",
                BusinessName = "Negocio Test",
                Address = "Calle 1 #23, Test City",
                Phone = "555-1234",
                WorkingHours = "Lunes a Viernes: 9:00 - 18:00",
                Services = ["Servicio A", "Servicio B"],
                AcceptedInsurance = ["Seguro X", "Seguro Y"],
                Pricing = new() { ["Servicio A"] = "$100", ["Servicio B"] = "$200" }
            }
        };
        _plugin = new BusinessInfoPlugin(_mockAdapter.Object, _tenantContext);
    }



    [Fact]
    public async Task GetProviderInfo_ShouldReturnProvidersFromAdapter()
    {
        _mockAdapter.Setup(a => a.GetAllProvidersAsync())
            .ReturnsAsync([
                new ServiceProvider { Name = "Dr. Test", Role = "General" }
            ]);

        var result = await _plugin.GetProviderInfo("todos");

        Assert.Contains("Dr. Test", result);
        Assert.Contains("General", result);
    }

    [Fact]
    public async Task GetProviderInfo_ShouldFilterByQuery()
    {
        _mockAdapter.Setup(a => a.SearchProvidersAsync("Retina"))
            .ReturnsAsync([
                new ServiceProvider { Name = "Dra. Ojos", Role = "Retina" }
            ]);

        var result = await _plugin.GetProviderInfo("Retina");

        Assert.Contains("Dra. Ojos", result);
        Assert.Contains("Retina", result);
    }
}
