using ReceptionistAgent.AI.Agents;
using ReceptionistAgent.AI.Configuration;
using ReceptionistAgent.AI.Plugins;
using ReceptionistAgent.Api.Middleware;
using ReceptionistAgent.Connectors.Adapters;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Security;
using ReceptionistAgent.Connectors.Services;
using ReceptionistAgent.Connectors.Messaging;
using ReceptionistAgent.Api.Security;
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
        var secretKey = builder.Configuration["Jwt:Key"] ?? "SUPER_SECRET_JWT_KEY_CHANGE_ME_IN_PRODUCTION!!!!";
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
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/dashboard"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// --- CORS for Admin Panel ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminPanel", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",  // Vite dev server (Admin Panel)
                "http://127.0.0.1:5173",
                "http://localhost:5174",  // Vite dev server (Client Dashboard)
                "http://127.0.0.1:5174",
                "http://localhost:4173")  // Vite preview
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

builder.Services.AddSingleton<SqlTenantRepository>(
    _ => new SqlTenantRepository(coreConnStr));

builder.Services.AddSingleton<ITenantResolver>(sp =>
{
    var inner = sp.GetRequiredService<SqlTenantRepository>();
    var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
    return new CachedTenantResolver(inner, cache);
});

// --- Tenant Configuration ---
// var tenantsConfig = new Dictionary<string, TenantConfiguration>(StringComparer.OrdinalIgnoreCase);
// var tenantsSection = builder.Configuration.GetSection("Tenants");

// foreach (var tenantSection in tenantsSection.GetChildren())
// {
//     var tenant = new TenantConfiguration
//     {
//         TenantId = tenantSection.Key,
//         BusinessName = tenantSection["BusinessName"] ?? "",
//         BusinessType = tenantSection["BusinessType"] ?? "",
//         DbType = tenantSection["DbType"] ?? "InMemory",
//         ConnectionString = tenantSection["ConnectionString"] ?? "",
//         TimeZoneId = tenantSection["TimeZoneId"] ?? "UTC",
//         Address = tenantSection["Address"] ?? "",
//         Phone = tenantSection["Phone"] ?? "",
//         WorkingHours = tenantSection["WorkingHours"] ?? "",
//         Services = tenantSection.GetSection("Services").Get<List<string>>() ?? [],
//         AcceptedInsurance = tenantSection.GetSection("AcceptedInsurance").Get<List<string>>() ?? [],
//         Pricing = tenantSection.GetSection("Pricing").Get<Dictionary<string, string>>() ?? new()
//     };

//     // Cargar providers del tenant
//     var providersSection = tenantSection.GetSection("Providers");
//     foreach (var provSection in providersSection.GetChildren())
//     {
//         tenant.Providers.Add(new TenantProviderConfig
//         {
//             Id = provSection["Id"] ?? "",
//             Name = provSection["Name"] ?? "",
//             Role = provSection["Role"] ?? "",
//             WorkingDays = provSection.GetSection("WorkingDays").Get<List<string>>() ?? [],
//             StartTime = provSection["StartTime"] ?? "09:00",
//             EndTime = provSection["EndTime"] ?? "18:00",
//             SlotDurationMinutes = int.TryParse(provSection["SlotDurationMinutes"], out var dur) ? dur : 30
//         });
//     }

//     tenantsConfig[tenant.TenantId] = tenant;
// }

// --- Application Core Services ---
//builder.Services.AddSingleton<ITenantResolver>(new InMemoryTenantResolver(tenantsConfig));
builder.Services.AddSingleton<ClientDataAdapterFactory>();
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
//
//var coreConnStr = builder.Configuration.GetConnectionString("AgentCore")!;
builder.Services.AddSingleton<IChatSessionRepository>(
    _ => new SqlChatSessionRepository(coreConnStr));

// --- Security & Audit Services ---
builder.Services.AddSingleton<IInputGuard, PromptInjectionGuard>();
builder.Services.AddSingleton<IOutputFilter, SensitiveDataFilter>();
//
builder.Services.AddSingleton<IAuditLogger>(
    _ => new SqlAuditLogger(coreConnStr));

// --- Billing, Reminders & Metrics ---
builder.Services.AddSingleton<IBillingService>(
    _ => new SqlBillingService(coreConnStr));
builder.Services.AddSingleton<IReminderService>(
    _ => new SqlReminderService(coreConnStr));
builder.Services.AddSingleton<SqlMetricsRepository>(
    _ => new SqlMetricsRepository(coreConnStr));

// Register HTTP Client for Meta API
builder.Services.AddHttpClient("MetaGraphApi");

// Register Factory for multi-tenant messaging (Twilio/Meta)
builder.Services.AddSingleton<IMessageSenderFactory, MessageSenderFactory>();

// Background service for sending reminders
builder.Services.AddHostedService<ReminderBackgroundService>();

builder.Services.AddTransient<ApiKeyAuthFilter>();

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
builder.Services.AddSingleton<IAIProviderConfigurator, GroqAIConfigurator>();
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
app.MapHealthChecks("/health");
app.MapHub<ReceptionistAgent.Api.Hubs.DashboardHub>("/hubs/dashboard");

app.Run();

// Necesario para WebApplicationFactory en tests de integración
public partial class Program { }
