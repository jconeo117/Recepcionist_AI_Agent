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

    // Credentials for Client Dashboard
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }

    [JsonPropertyName("timezoneId")]
    public string TimeZoneId { get; set; } = "UTC"; // ID de zona horaria (ej: "SA Pacific Standard Time" o "America/Bogota" dependiedo del OS)

    // Después de MessageProviderPhone:
    public string PhoneCountryCode { get; set; } = string.Empty; // ej: "57" para Colombia

    [JsonPropertyName("dbType")]
    public string DbType { get; set; } = "InMemory"; // "InMemory", "SqlServer", etc.

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = string.Empty;

    // Omnichannel Messaging Strategy
    public string MessageProvider { get; set; } = "Twilio"; // "Twilio" or "Meta"
    public string MessageProviderAccount { get; set; } = string.Empty; // Account SID for Twilio, PhoneNumberId for Meta
    public string MessageProviderToken { get; set; } = string.Empty;   // AuthToken for Twilio, AccessToken for Meta
    public string MessageProviderPhone { get; set; } = string.Empty;   // From number

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
    public string? WebhookUrl { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Define qué campos son obligatorios al agendar.
    /// Si es null, se usan defaults seguros (RequiresClientId = true).
    /// </summary>
    public BookingRequirements BookingRequirements { get; set; } = new();

    /// <summary>
    /// Modalidad del servicio. Afecta el flujo de preguntas del agente.
    /// </summary>
    public ServiceModality ServiceModality { get; set; } = ServiceModality.InPerson;

    /// <summary>
    /// Instrucciones personalizadas (System Prompt) para el agente de este tenant.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
}

/// <summary>
/// Define qué datos son obligatorios al agendar una cita para este tenant.
/// Los defaults se asignan automáticamente según el businessType al crear el tenant.
/// Los campos opcionales son guiados por el agente pero no bloquean el booking si se omiten.
/// </summary>
public class BookingRequirements
{
    // Identificación
    public bool RequiresClientId { get; set; } = true;

    // Datos de salud — activados para tenants médicos, base para FHIR R4
    public bool RequiresBirthDate { get; set; } = false;
    public bool RequiresGender { get; set; } = false;
    public bool RequiresInsurance { get; set; } = false;

    // Contacto
    public bool RequiresEmail { get; set; } = false;

    // Logística
    public bool RequiresAddress { get; set; } = false; // Activado automáticamente en modalidad AtHome

    // Extensible: campos personalizados por tenant (nombre del campo → obligatorio/opcional)
    // Ejemplo: { "mascota": true } para veterinarias
    public Dictionary<string, bool> CustomRequiredFields { get; set; } = new();
}

/// <summary>
/// Modalidad de prestación del servicio.
/// Afecta qué datos pide el agente y cómo redacta la confirmación.
/// </summary>
public enum ServiceModality
{
    InPerson = 0,  // Cliente va al negocio (default)
    AtHome = 1,    // Proveedor va donde el cliente → agente pide dirección
    Virtual = 2,   // Videollamada / llamada → agente pide plataforma preferida
    Hybrid = 3     // Agente pregunta preferencia al cliente
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
