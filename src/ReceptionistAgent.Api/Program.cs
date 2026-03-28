using ReceptionistAgent.AI.Agents;
using ReceptionistAgent.AI.Configuration;
using ReceptionistAgent.AI.Plugins;
using ReceptionistAgent.Api.Middleware;
using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Repositories;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Security;
using ReceptionistAgent.Connectors.Services;
using ReceptionistAgent.Connectors.Messaging;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Api.Workers;
using ReceptionistAgent.Api.Health;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Api.Services;
using ReceptionistAgent.AI.Services;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Tenant;
using Microsoft.SemanticKernel;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// Add JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var secretKey = builder.Configuration["Jwt:Key"] 
            ?? throw new InvalidOperationException(
                "La configuración 'Jwt:Key' no está definida. " +
                "Ejecute: dotnet user-secrets set \"Jwt:Key\" \"<clave-segura>\" " +
                "o defina la variable de entorno Jwt__Key en producción.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ReceptionistAI",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ReceptionistAI_ClientDashboard",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR: leer desde query string para hubs
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/dashboard"))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // Dashboard: leer desde httpOnly cookie 'auth_token'
                if (context.Request.Cookies.TryGetValue("auth_token", out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// --- CORS for Admin Panel ---
var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:5174", "http://localhost:4173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminPanel", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.OperationFilter<ReceptionistAgent.Api.Swagger.TenantHeaderOperationFilter>();
});
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();

// --- Rate Limiting Config ---
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("Global", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 60; // Max 60 requests per minute globally for API protectection
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var coreConnStr = builder.Configuration
    .GetConnectionString("AgentCore")!;

var isPostgres = coreConnStr.Contains("Host=", StringComparison.OrdinalIgnoreCase);

if (isPostgres)
{
    builder.Services.AddSingleton<ITenantResolver>(sp =>
    {
        var inner = new PostgreSqlTenantRepository(coreConnStr);
        var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        return new CachedTenantResolver(inner, cache);
    });
}
else
{
    builder.Services.AddSingleton<ITenantResolver>(sp =>
    {
        var inner = new SqlTenantRepository(coreConnStr);
        var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        return new CachedTenantResolver(inner, cache);
    });
}

// --- Application Core Services ---
builder.Services.AddSingleton<IDataAdapterProvider, SqlServerAdapterProvider>();
builder.Services.AddSingleton<IDataAdapterProvider, PostgreSqlAdapterProvider>();
builder.Services.AddSingleton<ClientDataAdapterFactory>();
builder.Services.AddSingleton<IDatabaseAdminRepository, DatabaseAdminRepository>();
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
builder.Services.AddScoped<IChatSessionRepository>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var coreConnStr = configuration.GetConnectionString("AgentCore")!;
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var tenant = tenantContext.CurrentTenant;
    var tenantConnStr = tenant?.ConnectionString;
    var providers = sp.GetRequiredService<IEnumerable<IDataAdapterProvider>>();

    var provider = providers.FirstOrDefault(p => p.Supports(tenant?.DbType ?? "SqlServer"));
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return provider!.CreateChatSessionRepository(coreConnStr, tenantConnStr, loggerFactory);
});

// --- Security & Audit Services ---
builder.Services.AddSingleton<ISessionBlacklistService, SessionBlacklistService>();
builder.Services.AddSingleton<IInputGuard, PromptInjectionGuard>();
builder.Services.AddSingleton<IOutputFilter, SensitiveDataFilter>();
// --- Security, Audit, Billing, Backups ---
if (isPostgres)
{
    builder.Services.AddSingleton<IAuditLogger>(_ => new PostgreSqlAuditLogger(coreConnStr));
    builder.Services.AddSingleton<IBillingService>(_ => new PostgreSqlBillingService(coreConnStr));
    builder.Services.AddSingleton<IBookingBackupService>(_ => new PostgreSqlBookingBackupService(coreConnStr));
}
else
{
    builder.Services.AddSingleton<IAuditLogger>(_ => new SqlAuditLogger(coreConnStr));
    builder.Services.AddSingleton<IBillingService>(_ => new SqlBillingService(coreConnStr));
    builder.Services.AddSingleton<IBookingBackupService>(_ => new SqlBookingBackupService(coreConnStr));
}

builder.Services.AddScoped<IReminderService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var coreConnStr = configuration.GetConnectionString("AgentCore")!;
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var tenant = tenantContext.CurrentTenant;
    var tenantConnStr = tenant?.ConnectionString;
    var providers = sp.GetRequiredService<IEnumerable<IDataAdapterProvider>>();

    var provider = providers.FirstOrDefault(p => p.Supports(tenant?.DbType ?? "SqlServer"));
    return provider!.CreateReminderService(coreConnStr, tenantConnStr);
});
if (isPostgres)
{
    builder.Services.AddSingleton<IMetricsRepository>(_ => new PostgreSqlMetricsRepository(coreConnStr));
}
else
{
    builder.Services.AddSingleton<IMetricsRepository>(_ => new SqlMetricsRepository(coreConnStr));
}

// Register HTTP Client for Meta API and Webhooks
builder.Services.AddHttpClient("MetaGraphApi");
builder.Services.AddHttpClient("WebhookClient");

// Register Factory for multi-tenant messaging (Twilio/Meta)
builder.Services.AddSingleton<IMessageSenderFactory, MessageSenderFactory>();
builder.Services.AddSingleton<IWebhookSenderService, WebhookSenderService>();

// Background service for sending reminders and processing outbox
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<OutboxWorker>();

// Register Advanced Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<AisServiceHealthCheck>("AI_Service")
    .AddCheck<MetaApiHealthCheck>("Meta_API")
    .AddCheck("Core_DB", new DatabaseHealthCheck(coreConnStr));

builder.Services.AddTransient<ApiKeyAuthFilter>();
builder.Services.AddTransient<TenantDbInitializer>();

// Scoped: resuelto por request via middleware
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<IEscalationService, EscalationService>();
builder.Services.AddScoped<ISessionContext, SessionContext>();

// IClientDataAdapter scoped: creado per-tenant via factory
builder.Services.AddScoped<IClientDataAdapter>(sp =>
{
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var factory = sp.GetRequiredService<ClientDataAdapterFactory>();

    if (!tenantContext.IsResolved)
        throw new InvalidOperationException("TenantContext no resuelto. ¿Se ejecutó TenantMiddleware?");

    return factory.CreateAdapter(tenantContext.CurrentTenant!);
});

builder.Services.AddScoped<IBookingService, BookingService>();

// --- Semantic Kernel & AI (Strategy Pattern) ---
builder.Services.AddSingleton<IAIProviderConfigurator, GoogleAIConfigurator>();
//builder.Services.AddSingleton<IAIProviderConfigurator, GroqAIConfigurator>();
builder.Services.AddSingleton<KernelFactory>();

builder.Services.AddScoped<IChatOrchestrator, ChatOrchestrator>();

builder.Services.AddScoped<IRecepcionistAgent>(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    var kernelFactory = sp.GetRequiredService<KernelFactory>();
    var config = sp.GetRequiredService<IConfiguration>();
    var provider = config["AI:Provider"] ?? "Google";
    var settings = kernelFactory.GetExecutionSettings(provider);
    return new RecepcionistAgent(kernel, settings);
});

builder.Services.AddScoped<Kernel>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var kernelFactory = sp.GetRequiredService<KernelFactory>();
    var provider = configuration["AI:Provider"] ?? "Google";

    var kernel = kernelFactory.CreateKernel(configuration, provider, sp);

    // Register Plugins (scoped: usan el adapter del tenant actual)
    var bookingService = sp.GetRequiredService<IBookingService>();
    var adapter = sp.GetRequiredService<IClientDataAdapter>();
    var tenantContext = sp.GetRequiredService<TenantContext>();
    var sessionContext = sp.GetRequiredService<ISessionContext>();
    var logger = sp.GetRequiredService<ILogger<BookingPlugin>>();
    var reminderService = sp.GetService<IReminderService>();
    var escalationService = sp.GetRequiredService<IEscalationService>();
    var escalationLogger = loggerFactory.CreateLogger<ReceptionistAgent.AI.Plugins.EscalationPlugin>();

    kernel.Plugins.AddFromObject(new BookingPlugin(bookingService, sessionContext, tenantContext, logger, reminderService), "BookingPlugin");
    kernel.Plugins.AddFromObject(new BusinessInfoPlugin(adapter, tenantContext), "BusinessInfoPlugin");
    kernel.Plugins.AddFromObject(new ReceptionistAgent.AI.Plugins.EscalationPlugin(escalationLogger, escalationService, tenantContext.CurrentTenant?.TenantId ?? "", sessionContext.SessionId), "EscalationPlugin");

    return kernel;
});

var app = builder.Build();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            results = report.Entries.Select(e => new
            {
                key = e.Key,
                value = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        };
        await context.Response.WriteAsJsonAsync(response);
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Solo redirigir a HTTPS en producción (ngrok y desarrollo usan HTTP)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// CORS for Admin Panel (antes de middlewares)
app.UseCors("AdminPanel");

// Autenticación primero para que el User.Claims esté disponible
app.UseAuthentication();
app.UseAuthorization();

// Tenant resolution middleware (antes de controllers)
app.UseMiddleware<TenantMiddleware>();

// Session context middleware (después de tenant)
app.UseMiddleware<SessionContextMiddleware>();
app.UseRateLimiter(); // Apply rate limiting BEFORE controllers

app.MapControllers();
// app.MapHealthChecks moved up
app.MapHub<ReceptionistAgent.Api.Hubs.DashboardHub>("/hubs/dashboard");

app.Run();

// Necesario para WebApplicationFactory en tests de integración
public partial class Program { }
