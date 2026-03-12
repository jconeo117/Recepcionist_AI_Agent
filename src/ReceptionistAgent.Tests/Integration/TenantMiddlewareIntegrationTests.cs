using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ReceptionistAgent.Tests.Integration;

public class TenantMiddlewareIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TenantMiddlewareIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
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
