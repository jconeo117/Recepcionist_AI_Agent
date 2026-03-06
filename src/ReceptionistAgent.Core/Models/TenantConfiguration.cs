using System.Text.Json.Serialization;

namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Configuración completa de un tenant.
/// En esta fase se carga desde appsettings.json.
/// La interfaz ITenantResolver está diseñada para que en el futuro
/// se pueda implementar un resolver que lea de base de datos.
/// </summary>
public class TenantConfiguration
{
    public string TenantId { get; set; } = string.Empty;
    [JsonPropertyName("businessName")]
    public string BusinessName { get; set; } = string.Empty;

    [JsonPropertyName("timezoneId")]
    public string TimeZoneId { get; set; } = "UTC"; // ID de zona horaria (ej: "SA Pacific Standard Time" o "America/Bogota" dependiedo del OS)

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = "InMemory"; // "InMemory", "SqlServer", etc.

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;  // "clinic", "salon", "workshop"
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string WorkingHours { get; set; } = string.Empty;
    public List<string> Services { get; set; } = [];
    public List<string> AcceptedInsurance { get; set; } = [];
    public Dictionary<string, string> Pricing { get; set; } = new();
    public List<TenantProviderConfig> Providers { get; set; } = [];
    public Dictionary<string, object> CustomSettings { get; set; } = new();

    // Persistence fields
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Configuración de un proveedor de servicio dentro de un tenant.
/// Se mapea a ServiceProvider cuando se crea el adapter.
/// </summary>
public class TenantProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> WorkingDays { get; set; } = [];       // "Monday", "Tuesday", etc.
    public string StartTime { get; set; } = "09:00";
    public string EndTime { get; set; } = "18:00";
    public int SlotDurationMinutes { get; set; } = 30;
}
