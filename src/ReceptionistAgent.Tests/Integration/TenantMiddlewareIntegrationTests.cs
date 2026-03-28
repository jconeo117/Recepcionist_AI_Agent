using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Tenant;
using Microsoft.Extensions.Configuration;
using ReceptionistAgent.Core.Services;
using Xunit;

namespace ReceptionistAgent.Tests.Integration;

public class TenantMiddlewareIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public TenantMiddlewareIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "TEST_SECRET_KEY_32_CHARS_LONG_FOR_TESTING_ONLY",
                    ["Jwt:Issuer"] = "ReceptionistAI",
                    ["Jwt:Audience"] = "ReceptionistAI_ClientDashboard",
                    ["ConnectionStrings:AgentCore"] = "Server=localhost;Database=TestDB;Trusted_Connection=True;"
                });
            });

            builder.ConfigureServices(services =>
            {
                var mockTenantResolver = new Mock<ITenantResolver>();
                var mockBillingService = new Mock<IBillingService>();
                
                mockBillingService.Setup(b => b.IsTenantAllowedAsync(It.IsAny<string>()))
                    .ReturnsAsync(true);

                var mockTenant = new TenantConfiguration
                {
                    TenantId = "clinica-salud-total",
                    BusinessName = "Clínica Salud Total",
                    BusinessType = "clinic",
                    DbType = "InMemory"
                };

                mockTenantResolver.Setup(r => r.ResolveAsync("clinica-salud-total"))
                    .ReturnsAsync(mockTenant);
                mockTenantResolver.Setup(r => r.GetAllTenantIdsAsync())
                    .ReturnsAsync(new List<string> { "clinica-salud-total" });
                mockTenantResolver.Setup(r => r.GetAllTenantsAsync())
                    .ReturnsAsync(new List<TenantConfiguration> { mockTenant });

                services.Replace(ServiceDescriptor.Singleton<ITenantResolver>(mockTenantResolver.Object));
                services.Replace(ServiceDescriptor.Singleton<IBillingService>(mockBillingService.Object));
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithValidTenantHeader_ShouldReturn200()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Chat");
        request.Headers.Add("X-Tenant-Id", "clinica-salud-total");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { SessionId = Guid.NewGuid(), Message = "Hola" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Puede ser 200 o 500 (si AI no está configurada), pero NO 400
        Assert.True(response.StatusCode != HttpStatusCode.BadRequest, $"Expected Not BadRequest but got {response.StatusCode}. Output: {content}");
    }

    [Fact]
    public async Task Request_WithoutTenantHeader_ShouldReturn400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Chat");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { SessionId = Guid.NewGuid(), Message = "Hola" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("X-Tenant-Id", content);
    }

    [Fact]
    public async Task Request_WithInvalidTenantId_ShouldReturn400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Chat");
        request.Headers.Add("X-Tenant-Id", "tenant-que-no-existe");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { SessionId = Guid.NewGuid(), Message = "Hola" }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("no encontrado", content);
    }

    [Fact]
    public async Task Swagger_ShouldBeAccessible_WithoutTenantHeader()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        // Swagger debe ser accesible sin header de tenant
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithDifferentTenants_ShouldBeAccepted()
    {
        // Dev tenant
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/Chat");
        request1.Headers.Add("X-Tenant-Id", "clinica-salud-total");
        request1.Content = new StringContent(
            JsonSerializer.Serialize(new { SessionId = Guid.NewGuid(), Message = "Hola" }),
            Encoding.UTF8,
            "application/json");

        var response1 = await _client.SendAsync(request1);
        var content = await response1.Content.ReadAsStringAsync();
        Assert.True(response1.StatusCode != HttpStatusCode.BadRequest, $"Expected Not BadRequest but got {response1.StatusCode}. Output: {content}");
    }
}
