using System.Collections.Generic;

namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Genera los BookingRequirements adecuados para cada tipo de negocio.
/// Usado al crear un tenant nuevo desde el Admin Panel.
/// Puede ser sobreescrito manualmente por el administrador.
/// </summary>
public static class BookingRequirementsFactory
{
    public static BookingRequirements CreateFor(string businessType) =>
        businessType?.ToLowerInvariant() switch
        {
            // SALUD — campos requeridos para FHIR R4
            "clinic" or "hospital" or "clinica" => new BookingRequirements
            {
                RequiresClientId = true,
                RequiresBirthDate = true,
                RequiresGender = true,
                RequiresInsurance = true,
                RequiresEmail = false,
            },
            "dental" or "odontologia" => new BookingRequirements
            {
                RequiresClientId = true,
                RequiresBirthDate = true,
                RequiresGender = false,
                RequiresInsurance = true,
            },
            "veterinary" or "veterinaria" => new BookingRequirements
            {
                RequiresClientId = true,
                RequiresBirthDate = false,
                // Campo personalizado para el nombre de la mascota
                CustomRequiredFields = new Dictionary<string, bool>
                {
                    { "petName",    true  },
                    { "petSpecies", true  },
                    { "petAge",     false }
                }
            },
            "psychology" or "psicologia" or "therapy" => new BookingRequirements
            {
                RequiresClientId = true,
                RequiresBirthDate = true,
                RequiresGender = true,
                RequiresInsurance = false, // Frecuente pago particular
            },

            // CUIDADO PERSONAL — sin documento de identidad
            "salon" or "beauty" or "peluqueria" => new BookingRequirements
            {
                RequiresClientId = false,
                RequiresEmail = false,
            },
            "nails" or "nail_salon" or "unas" => new BookingRequirements
            {
                RequiresClientId = false,
                RequiresEmail = false,
            },
            "spa" or "wellness" or "masajes" => new BookingRequirements
            {
                RequiresClientId = false,
                RequiresEmail = false,
            },
            "barbershop" or "barberia" => new BookingRequirements
            {
                RequiresClientId = false,
            },

            // FITNESS
            "gym" or "fitness" or "gimnasio" => new BookingRequirements
            {
                RequiresClientId = false,
                RequiresEmail = true, // Para enviar rutinas
            },
            "yoga" or "pilates" => new BookingRequirements
            {
                RequiresClientId = false,
            },

            // SERVICIOS TÉCNICOS
            "workshop" or "taller" or "mecanico" => new BookingRequirements
            {
                RequiresClientId = true, // Necesario para documentos de garantía
                CustomRequiredFields = new Dictionary<string, bool>
                {
                    { "vehiclePlate", true  },
                    { "vehicleBrand", false }
                }
            },

            // EDUCACIÓN
            "tutoring" or "tutoria" or "academia" => new BookingRequirements
            {
                RequiresClientId = false,
                RequiresEmail = true, // Para enviar materiales
            },

            // DEFAULT — conservador, funciona para cualquier negocio
            _ => new BookingRequirements
            {
                RequiresClientId = true,
            }
        };

    /// <summary>
    /// Modalidad de servicio sugerida por tipo de negocio.
    /// Los tenants a domicilio (plomeros, electricistas, masajes a domicilio)
    /// tienen AtHome como default.
    /// </summary>
    public static ServiceModality ModalityFor(string businessType) =>
        businessType?.ToLowerInvariant() switch
        {
            "plumbing" or "plomeria" => ServiceModality.AtHome,
            "electrical" or "electricista" => ServiceModality.AtHome,
            "cleaning" or "limpieza" => ServiceModality.AtHome,
            "home_spa" or "spa_domicilio" => ServiceModality.AtHome,
            "tutoring" or "tutoria" => ServiceModality.Hybrid,
            "therapy" or "terapia" => ServiceModality.Hybrid,
            "psychology" or "psicologia" => ServiceModality.Hybrid,
            _ => ServiceModality.InPerson
        };
}
