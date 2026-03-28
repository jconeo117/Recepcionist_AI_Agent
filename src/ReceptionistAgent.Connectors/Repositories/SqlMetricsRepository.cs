using Dapper;
using Microsoft.Data.SqlClient;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Repositories;

namespace ReceptionistAgent.Connectors.Repositories;

public class SqlMetricsRepository : IMetricsRepository
{
    private readonly string _connectionString;

    public SqlMetricsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<MetricsSummary> GetMetricsAsync(string? tenantId, DateTime from, DateTime to)
    {
        using var connection = new SqlConnection(_connectionString);

        // Mensajes totales
        var messagesSql = tenantId != null
            ? "SELECT COUNT(*) FROM AuditLog WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To AND EventType LIKE '%UserMessage'"
            : "SELECT COUNT(*) FROM AuditLog WHERE Timestamp BETWEEN @From AND @To AND EventType LIKE '%UserMessage'";
        var totalMessages = await connection.QuerySingleAsync<int>(messagesSql, new { TenantId = tenantId, From = from, To = to });

        // Security blocks
        var blocksSql = tenantId != null
            ? "SELECT COUNT(*) FROM AuditLog WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To AND EventType = 'SecurityBlock'"
            : "SELECT COUNT(*) FROM AuditLog WHERE Timestamp BETWEEN @From AND @To AND EventType = 'SecurityBlock'";
        var securityBlocks = await connection.QuerySingleAsync<int>(blocksSql, new { TenantId = tenantId, From = from, To = to });

        // Mensajes por día
        var perDaySql = tenantId != null
            ? @"SELECT CAST(Timestamp AS DATE) as [Date], COUNT(*) as [Count]
                FROM AuditLog WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To AND EventType LIKE '%UserMessage'
                GROUP BY CAST(Timestamp AS DATE) ORDER BY [Date]"
            : @"SELECT CAST(Timestamp AS DATE) as [Date], COUNT(*) as [Count]
                FROM AuditLog WHERE Timestamp BETWEEN @From AND @To AND EventType LIKE '%UserMessage'
                GROUP BY CAST(Timestamp AS DATE) ORDER BY [Date]";
        var messagesPerDay = (await connection.QueryAsync<DailyCount>(perDaySql, new { TenantId = tenantId, From = from, To = to })).ToList();

        // Sessions únicas
        var sessionsSql = tenantId != null
            ? "SELECT COUNT(DISTINCT SessionId) FROM AuditLog WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To"
            : "SELECT COUNT(DISTINCT SessionId) FROM AuditLog WHERE Timestamp BETWEEN @From AND @To";
        var uniqueSessions = await connection.QuerySingleAsync<int>(sessionsSql, new { TenantId = tenantId, From = from, To = to });

        // Bookings count
        var bookingsSql = tenantId != null
            ? "SELECT COUNT(*) FROM AuditLog WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To AND EventType = 'PluginCall' AND Content LIKE '%BookAppointment%'"
            : "SELECT COUNT(*) FROM AuditLog WHERE Timestamp BETWEEN @From AND @To AND EventType = 'PluginCall' AND Content LIKE '%BookAppointment%'";
        var totalBookings = await connection.QuerySingleAsync<int>(bookingsSql, new { TenantId = tenantId, From = from, To = to });

        // Sessions with at least one booking
        var sessionsWithBookingSql = tenantId != null
            ? @"SELECT COUNT(DISTINCT SessionId) FROM AuditLog 
                WHERE TenantId = @TenantId AND Timestamp BETWEEN @From AND @To 
                AND (EventType = 'PluginCall' AND Content LIKE '%BookAppointment%')"
            : @"SELECT COUNT(DISTINCT SessionId) FROM AuditLog 
                WHERE Timestamp BETWEEN @From AND @To 
                AND (EventType = 'PluginCall' AND Content LIKE '%BookAppointment%')";
        var sessionsWithBooking = await connection.QuerySingleAsync<int>(sessionsWithBookingSql, new { TenantId = tenantId, From = from, To = to });

        var conversionRate = uniqueSessions > 0 ? Math.Round((double)sessionsWithBooking / uniqueSessions * 100, 2) : 0;

        // Average time to booking (seconds from first message to booking event per session)
        var avgTimeSql = tenantId != null
            ? @"SELECT AVG(DATEDIFF(SECOND, FirstMsg, BookingMsg)) FROM (
                    SELECT a.SessionId, 
                           MIN(CASE WHEN a.EventType LIKE '%UserMessage' THEN a.Timestamp END) as FirstMsg,
                           MIN(CASE WHEN a.EventType = 'PluginCall' AND a.Content LIKE '%BookAppointment%' THEN a.Timestamp END) as BookingMsg
                    FROM AuditLog a
                    WHERE a.TenantId = @TenantId AND a.Timestamp BETWEEN @From AND @To
                    GROUP BY a.SessionId
                    HAVING MIN(CASE WHEN a.EventType = 'PluginCall' AND a.Content LIKE '%BookAppointment%' THEN a.Timestamp END) IS NOT NULL
                ) sub"
            : @"SELECT AVG(DATEDIFF(SECOND, FirstMsg, BookingMsg)) FROM (
                    SELECT a.SessionId,
                           MIN(CASE WHEN a.EventType LIKE '%UserMessage' THEN a.Timestamp END) as FirstMsg,
                           MIN(CASE WHEN a.EventType = 'PluginCall' AND a.Content LIKE '%BookAppointment%' THEN a.Timestamp END) as BookingMsg
                    FROM AuditLog a
                    WHERE a.Timestamp BETWEEN @From AND @To
                    GROUP BY a.SessionId
                    HAVING MIN(CASE WHEN a.EventType = 'PluginCall' AND a.Content LIKE '%BookAppointment%' THEN a.Timestamp END) IS NOT NULL
                ) sub";
        var avgTimeToBooking = await connection.QuerySingleOrDefaultAsync<double?>(avgTimeSql, new { TenantId = tenantId, From = from, To = to }) ?? 0;

        return new MetricsSummary
        {
            TenantId = tenantId ?? "all",
            From = from,
            To = to,
            TotalMessages = totalMessages,
            SecurityBlocks = securityBlocks,
            UniqueSessions = uniqueSessions,
            MessagesPerDay = messagesPerDay,
            TotalBookings = totalBookings,
            ConversionRate = conversionRate,
            AbandonmentRate = Math.Round(100 - conversionRate, 2),
            AverageTimeToBookingSeconds = Math.Round(avgTimeToBooking, 2)
        };
    }
}
