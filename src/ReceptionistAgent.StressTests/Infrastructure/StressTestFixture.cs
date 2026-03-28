using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReceptionistAgent.Connectors.Repositories;
using ReceptionistAgent.Connectors.Security;
using ReceptionistAgent.Core.Security;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Repositories;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Session;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.StressTests.Infrastructure;

/// <summary>
/// WebApplicationFactory customizado para pruebas de estrés.
/// Levanta el servidor API completo en memoria con:
/// - Tenant de prueba InMemory (no requiere DB)
/// - Audit logger en memoria (no requiere SQL Server/PostgreSQL)
/// - Chat sessions en memoria
/// - Billing service siempre permitido
/// - Rate limiting relajado para pruebas de concurrencia
/// </summary>
public class StressTestFixture : WebApplicationFactory<Program>
{
    public const string ClinicTenantId = "stress-test-clinic";
    public const string BeautySalonTenantId = "stress-test-beauty";
    public const string NailSalonTenantId = "stress-test-nails";

    private static TenantConfiguration CreateClinicTenant() => new()
    {
        TenantId = ClinicTenantId,
        BusinessName = "Clínica Visual del Valle",
        BusinessType = "clinic",
        DbType = "InMemory",
        ConnectionString = "",
        TimeZoneId = "America/Bogota",
        Address = "Calle 5 #23-47, Cali, Colombia",
        Phone = "602-555-1234",
        WorkingHours = "Lunes a Viernes: 8:00 AM - 6:00 PM, Sábados: 8:00 AM - 12:00 PM",
        PhoneCountryCode = "57",
        Services =
        [
            "Consulta Oftalmología General",
            "Examen de Retina",
            "Cirugía de Cataratas",
            "Adaptación de Lentes de Contacto",
            "Control de Glaucoma"
        ],
        AcceptedInsurance = ["Sura EPS", "Nueva EPS", "Sanitas", "Particular"],
        Pricing = new Dictionary<string, string>
        {
            ["Consulta General"] = "$80.000 COP",
            ["Examen de Retina"] = "$150.000 COP",
            ["Adaptación de Lentes"] = "$120.000 COP"
        },
        Providers =
        [
            new TenantProviderConfig
            {
                Id = "DR001",
                Name = "Dr. Carlos Ramírez",
                Role = "Oftalmología General",
                WorkingDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
                StartTime = "08:00",
                EndTime = "18:00",
                SlotDurationMinutes = 30
            },
            new TenantProviderConfig
            {
                Id = "DR002",
                Name = "Dra. María González",
                Role = "Especialista en Retina",
                WorkingDays = ["Monday", "Wednesday", "Friday"],
                StartTime = "09:00",
                EndTime = "16:00",
                SlotDurationMinutes = 30
            },
            new TenantProviderConfig
            {
                Id = "DR003",
                Name = "Dr. Andrés Martínez",
                Role = "Cirugía de Cataratas",
                WorkingDays = ["Tuesday", "Thursday"],
                StartTime = "07:00",
                EndTime = "14:00",
                SlotDurationMinutes = 45
            }
        ]
    };

    private static TenantConfiguration CreateBeautySalonTenant() => new()
    {
        TenantId = BeautySalonTenantId,
        BusinessName = "Elegance Hair & Beauty",
        BusinessType = "BeautySalon",
        DbType = "InMemory",
        ConnectionString = "",
        TimeZoneId = "America/Bogota",
        Address = "Av. Siempre Viva 742",
        Phone = "602-555-5678",
        WorkingHours = "Lunes a Sábado: 9:00 AM - 8:00 PM",
        PhoneCountryCode = "57",
        Services = ["Corte de Cabello", "Tinte", "Peinado", "Maquillaje", "Keratina"],
        AcceptedInsurance = [],
        Pricing = new Dictionary<string, string>
        {
            ["Corte de Cabello"] = "$40.000 COP",
            ["Tinte"] = "$120.000 COP",
            ["Keratina"] = "$200.000 COP"
        },
        Providers = [
            new TenantProviderConfig
            {
                Id = "STY001",
                Name = "Estilista Roberto",
                Role = "Colorista",
                WorkingDays = ["Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
                StartTime = "10:00",
                EndTime = "19:00",
                SlotDurationMinutes = 60
            },
            new TenantProviderConfig
            {
                Id = "STY002",
                Name = "Estilista Sofía",
                Role = "Cortes y Peinados",
                WorkingDays = ["Monday", "Wednesday", "Friday", "Saturday"],
                StartTime = "09:00",
                EndTime = "18:00",
                SlotDurationMinutes = 45
            }
        ]
    };

    private static TenantConfiguration CreateNailSalonTenant() => new()
    {
        TenantId = NailSalonTenantId,
        BusinessName = "Magic Nails Spa",
        BusinessType = "NailSalon",
        DbType = "InMemory",
        ConnectionString = "",
        TimeZoneId = "America/Bogota",
        Address = "Centro Comercial La Luna Local 15",
        Phone = "602-555-9012",
        WorkingHours = "Lunes a Domingo: 10:00 AM - 7:00 PM",
        PhoneCountryCode = "57",
        Services = ["Manicura Tradicional", "Pedicura", "Uñas Acrílicas", "Semipermanente"],
        AcceptedInsurance = [],
        Pricing = new Dictionary<string, string>
        {
            ["Manicura"] = "$25.000 COP",
            ["Pedicura"] = "$30.000 COP",
            ["Uñas Acrílicas"] = "$80.000 COP"
        },
        Providers = [
            new TenantProviderConfig
            {
                Id = "NAIL001",
                Name = "Manicurista Camila",
                Role = "Especialista en Acrílico",
                WorkingDays = ["Wednesday", "Thursday", "Friday", "Saturday", "Sunday"],
                StartTime = "10:00",
                EndTime = "19:00",
                SlotDurationMinutes = 90
            }
        ]
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Override tenant resolver with InMemory containing test tenants
            services.RemoveAll<ITenantResolver>();
            var testTenants = new Dictionary<string, TenantConfiguration>
            {
                [ClinicTenantId] = CreateClinicTenant(),
                [BeautySalonTenantId] = CreateBeautySalonTenant(),
                [NailSalonTenantId] = CreateNailSalonTenant()
            };
            services.AddSingleton<ITenantResolver>(new InMemoryTenantResolver(testTenants));

            // Override audit logger with InMemory (no DB needed)
            services.RemoveAll<IAuditLogger>();
            services.AddSingleton<IAuditLogger, InMemoryAuditLogger>();

            // Override chat session repository with InMemory
            services.RemoveAll<IChatSessionRepository>();
            services.AddSingleton<IChatSessionRepository, InMemoryChatSessionRepository>();

            // Override billing service: always allow access
            services.RemoveAll<IBillingService>();
            services.AddSingleton<IBillingService>(new AlwaysAllowedBillingService());

            // Override booking backup service with no-op
            services.RemoveAll<IBookingBackupService>();
            services.AddSingleton<IBookingBackupService>(new NoOpBookingBackupService());

            // Override reminder service with no-op
            services.RemoveAll<IReminderService>();
            services.AddScoped<IReminderService>(sp => new NoOpReminderService());

            // Override metrics repository with no-op
            services.RemoveAll<IMetricsRepository>();
            services.AddSingleton<IMetricsRepository>(new NoOpMetricsRepository());
        });
    }

    /// <summary>
    /// Creates an HttpClient pre-configured with the requested test tenant header.
    /// Defaults to Clinic tenant.
    /// </summary>
    public HttpClient CreateTenantClient(string tenantId = ClinicTenantId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        client.Timeout = TimeSpan.FromMinutes(2); // LLM calls can be slow
        return client;
    }
}

// ═══ Stub implementations for test isolation ═══

internal class AlwaysAllowedBillingService : IBillingService
{
    public Task<TenantBilling?> GetBillingAsync(string tenantId) =>
        Task.FromResult<TenantBilling?>(new TenantBilling
        {
            TenantId = tenantId,
            BillingStatus = BillingStatus.Active,
            ActiveUntil = DateTime.UtcNow.AddYears(1)
        });

    public Task<bool> IsTenantAllowedAsync(string tenantId) =>
        Task.FromResult(true);

    public Task SuspendTenantAsync(string tenantId, string reason) =>
        Task.CompletedTask;

    public Task ReactivateTenantAsync(string tenantId, DateTime activeUntil) =>
        Task.CompletedTask;

    public Task UpdateBillingAsync(TenantBilling billing) =>
        Task.CompletedTask;

    public Task CreateBillingAsync(TenantBilling billing) =>
        Task.CompletedTask;
}

internal class NoOpBookingBackupService : IBookingBackupService
{
    public Task BackupAsync(BookingRecord booking, string tenantId) => Task.CompletedTask;
    public Task UpdateStatusBackupAsync(Guid bookingId, string tenantId, BookingStatus status) => Task.CompletedTask;
}

internal class NoOpReminderService : IReminderService
{
    public Task ScheduleRemindersForBookingAsync(BookingRecord booking, string recipientPhone, string countryCode = "", string timeZoneId = "UTC") => Task.CompletedTask;
    public Task<List<Reminder>> GetPendingRemindersAsync(DateTime before) => Task.FromResult(new List<Reminder>());
    public Task MarkAsSentAsync(Guid reminderId) => Task.CompletedTask;
    public Task MarkAsFailedAsync(Guid reminderId, string error) => Task.CompletedTask;
    public Task CancelRemindersForBookingAsync(Guid bookingId) => Task.CompletedTask;
}

internal class NoOpMetricsRepository : IMetricsRepository
{
    public Task<MetricsSummary> GetMetricsAsync(string? tenantId, DateTime from, DateTime to) =>
        Task.FromResult(new MetricsSummary());
}
