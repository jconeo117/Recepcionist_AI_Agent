using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Tenant;
using Microsoft.AspNetCore.Http;

namespace ReceptionistAgent.Api.Middleware;

/// <summary>
/// Middleware que resuelve el tenant a partir del header X-Tenant-Id.
/// Si el header no está presente o el tenant no existe, retorna 400.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private const string TenantHeaderName = "X-Tenant-Id";

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantResolver tenantResolver, TenantContext tenantContext)
    {
        // Permitir Swagger, health checks, audit y admin sin tenant
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/swagger") || path.StartsWith("/health")
            || path.StartsWith("/api/audit") || path.StartsWith("/api/admin"))
        {
            await _next(context);
            return;
        }

        // Extraer de Header primero
        string? tenantId = context.Request.Headers[TenantHeaderName];

        // Fallback para webhooks de Twilio (vienen en la URL: /api/twilio/{tenantId})
        if (string.IsNullOrWhiteSpace(tenantId) && path.StartsWith("/api/twilio/"))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[1] == "twilio")
            {
                tenantId = parts[2];
            }
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Header 'X-Tenant-Id' es requerido.\"}");
            return;
        }

        var tenant = await tenantResolver.ResolveAsync(tenantId!);

        if (tenant == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            var allTenants = await tenantResolver.GetAllTenantIdsAsync();
            await context.Response.WriteAsync(
                $"{{\"error\":\"Tenant '{tenantId}' no encontrado.\",\"available\":[{string.Join(",", allTenants.Select(t => $"\"{t}\""))}]}}");
            return;
        }

        // Verificar acceso por facturación (activo y no expirado)
        var billingService = context.RequestServices.GetService<IBillingService>();
        if (billingService != null && !await billingService.IsTenantAllowedAsync(tenantId!))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Acceso suspendido o expirado. Contacte al administrador para reactivar su cuenta.\"}");
            return;
        }

        tenantContext.CurrentTenant = tenant;
        await _next(context);
    }
}
