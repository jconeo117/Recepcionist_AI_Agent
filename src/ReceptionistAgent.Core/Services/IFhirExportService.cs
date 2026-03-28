using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReceptionistAgent.Core.Models;

namespace ReceptionistAgent.Core.Services;

/// <summary>
/// Exporta datos de booking al formato FHIR R4.
/// Solo aplica a tenants con businessType de salud.
/// </summary>
public interface IFhirExportService
{
    /// <summary>
    /// Genera un FHIR Patient resource a partir de los datos del cliente en un booking.
    /// </summary>
    Task<FhirPatientDto> ExportPatientAsync(BookingRecord booking, string tenantId);
    
    /// <summary>
    /// Genera un FHIR Appointment resource a partir de un booking.
    /// </summary>
    Task<FhirAppointmentDto> ExportAppointmentAsync(BookingRecord booking, TenantConfiguration tenant);
    
    /// <summary>
    /// Exporta todos los bookings de un tenant como un FHIR Bundle.
    /// </summary>
    Task<string> ExportBundleAsJsonAsync(string tenantId, DateTime from, DateTime to);
}

// DTOs intermedios — independientes de cualquier librería FHIR externa
public record FhirPatientDto(
    string Id,
    string FamilyName,
    string GivenName,
    string? BirthDate,
    string? Gender,
    string? IdentifierSystem,
    string? IdentifierValue,
    string? Phone
);

public record FhirAppointmentDto(
    string Id,
    string Status,
    string PatientId,
    string PractitionerId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description,
    string? LocationAddress
);
