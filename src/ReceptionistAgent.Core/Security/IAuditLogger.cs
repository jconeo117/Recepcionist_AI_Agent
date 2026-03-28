using ReceptionistAgent.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReceptionistAgent.Core.Security;

/// <summary>
/// Registra eventos de auditoría para cada interacción con el agente y el sistema.
/// Permite consultar historial de sesiones y eventos de seguridad.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry);
    Task<List<AuditEntry>> GetSessionAuditAsync(Guid sessionId);
    Task<List<AuditEntry>> GetSecurityEventsAsync(string? tenantId, DateTime from, DateTime to);
    Task<List<AuditEntry>> GetAllEventsAsync(string? tenantId, int limit = 100);
}
