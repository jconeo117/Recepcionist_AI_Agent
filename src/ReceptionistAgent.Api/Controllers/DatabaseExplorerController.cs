using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Dapper;
using ReceptionistAgent.Api.Security;
using ReceptionistAgent.Core.Tenant;

namespace ReceptionistAgent.Api.Controllers;

/// <summary>
/// Endpoint de exploración de base de datos de tenants.
/// Permite inspeccionar el esquema y datos de bookings de la DB de un tenant.
/// Protegido con API Key.
/// </summary>
[ApiController]
[Route("api/admin/tenants/{tenantId}/database")]
[EnableRateLimiting("Global")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class DatabaseExplorerController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;

    public DatabaseExplorerController(ITenantResolver tenantResolver)
    {
        _tenantResolver = tenantResolver;
    }

    /// <summary>
    /// Obtiene el esquema de la tabla Bookings del tenant.
    /// </summary>
    [HttpGet("schema")]
    public async Task<IActionResult> GetSchema(string tenantId)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        if (!tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Solo se puede explorar la base de datos de tenants con DbType 'SqlServer'." });

        if (string.IsNullOrWhiteSpace(tenant.ConnectionString))
            return BadRequest(new { error = "El tenant no tiene una ConnectionString configurada." });

        try
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            await connection.OpenAsync();

            // Get table list
            var tables = (await connection.QueryAsync<TableInfo>(
                @"SELECT TABLE_NAME as TableName, TABLE_TYPE as TableType 
                  FROM INFORMATION_SCHEMA.TABLES 
                  WHERE TABLE_TYPE = 'BASE TABLE' 
                  ORDER BY TABLE_NAME"
            )).ToList();

            // Get columns for the Bookings table (primary focus)
            var bookingsColumns = (await connection.QueryAsync<ColumnInfo>(
                @"SELECT 
                    COLUMN_NAME as ColumnName,
                    DATA_TYPE as DataType,
                    IS_NULLABLE as IsNullable,
                    CHARACTER_MAXIMUM_LENGTH as MaxLength,
                    COLUMN_DEFAULT as DefaultValue,
                    ORDINAL_POSITION as OrdinalPosition
                  FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = 'Bookings'
                  ORDER BY ORDINAL_POSITION"
            )).ToList();

            return Ok(new
            {
                tenantId,
                dbType = tenant.DbType,
                tables,
                bookingsSchema = bookingsColumns
            });
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new { error = $"Error al conectar con la base de datos: {ex.Message}" });
        }
    }

    /// <summary>
    /// Obtiene los bookings del tenant directamente desde su base de datos.
    /// </summary>
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings(string tenantId, [FromQuery] int limit = 50)
    {
        var tenant = await _tenantResolver.ResolveAsync(tenantId);
        if (tenant == null)
            return NotFound(new { error = $"Tenant '{tenantId}' no encontrado." });

        if (!tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Solo se puede explorar la base de datos de tenants con DbType 'SqlServer'." });

        if (string.IsNullOrWhiteSpace(tenant.ConnectionString))
            return BadRequest(new { error = "El tenant no tiene una ConnectionString configurada." });

        try
        {
            limit = Math.Clamp(limit, 1, 200);

            using var connection = new SqlConnection(tenant.ConnectionString);
            await connection.OpenAsync();

            var bookings = (await connection.QueryAsync<dynamic>(
                $"SELECT TOP (@Limit) * FROM Bookings ORDER BY CreatedAt DESC",
                new { Limit = limit }
            )).ToList();

            var totalCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Bookings"
            );

            return Ok(new
            {
                tenantId,
                totalBookings = totalCount,
                showing = bookings.Count,
                bookings
            });
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new { error = $"Error al consultar bookings: {ex.Message}" });
        }
    }
}

public record TableInfo(string TableName, string TableType);
public record ColumnInfo(
    string ColumnName, string DataType, string IsNullable,
    int? MaxLength, string? DefaultValue, int OrdinalPosition
);
