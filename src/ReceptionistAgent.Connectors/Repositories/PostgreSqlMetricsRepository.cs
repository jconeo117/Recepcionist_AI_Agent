using Dapper;
using Npgsql;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Repositories;

namespace ReceptionistAgent.Connectors.Repositories;

public class PostgreSqlMetricsRepository : IMetricsRepository
{
    private readonly string _connectionString;

    public PostgreSqlMetricsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ReceptionistAgent.Core.Models.MetricsSummary> GetMetricsAsync(string? tenantId, DateTime from, DateTime to)
    {
        using var connection = new NpgsqlConnection(_connectionString);

        // Mensajes totales
        var messagesSql = tenantId != null
            ? "SELECT COUNT(*) FROM audits WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To AND event_type LIKE '%UserMessage'"
            : "SELECT COUNT(*) FROM audits WHERE timestamp BETWEEN @From AND @To AND event_type LIKE '%UserMessage'";
        var totalMessages = await connection.QuerySingleAsync<int>(messagesSql, new { TenantId = tenantId, From = from, To = to });

        // Security blocks
        var blocksSql = tenantId != null
            ? "SELECT COUNT(*) FROM audits WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To AND event_type = 'SecurityBlock'"
            : "SELECT COUNT(*) FROM audits WHERE timestamp BETWEEN @From AND @To AND event_type = 'SecurityBlock'";
        var securityBlocks = await connection.QuerySingleAsync<int>(blocksSql, new { TenantId = tenantId, From = from, To = to });

        // Mensajes por día
        var perDaySql = tenantId != null
            ? @"SELECT date_trunc('day', timestamp) as ""Date"", COUNT(*) as ""Count""
                FROM audits WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To AND event_type LIKE '%UserMessage'
                GROUP BY date_trunc('day', timestamp) ORDER BY ""Date"""
            : @"SELECT date_trunc('day', timestamp) as ""Date"", COUNT(*) as ""Count""
                FROM audits WHERE timestamp BETWEEN @From AND @To AND event_type LIKE '%UserMessage'
                GROUP BY date_trunc('day', timestamp) ORDER BY ""Date""";
        var messagesPerDay = (await connection.QueryAsync<DailyCount>(perDaySql, new { TenantId = tenantId, From = from, To = to })).ToList();

        // Sessions únicas
        var sessionsSql = tenantId != null
            ? "SELECT COUNT(DISTINCT session_id) FROM audits WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To"
            : "SELECT COUNT(DISTINCT session_id) FROM audits WHERE timestamp BETWEEN @From AND @To";
        var uniqueSessions = await connection.QuerySingleAsync<int>(sessionsSql, new { TenantId = tenantId, From = from, To = to });

        // Bookings count
        var bookingsSql = tenantId != null
            ? "SELECT COUNT(*) FROM audits WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To AND event_type = 'PluginCall' AND content LIKE '%BookAppointment%'"
            : "SELECT COUNT(*) FROM audits WHERE timestamp BETWEEN @From AND @To AND event_type = 'PluginCall' AND content LIKE '%BookAppointment%'";
        var totalBookings = await connection.QuerySingleAsync<int>(bookingsSql, new { TenantId = tenantId, From = from, To = to });

        // Sessions with at least one booking
        var sessionsWithBookingSql = tenantId != null
            ? @"SELECT COUNT(DISTINCT session_id) FROM audits 
                WHERE tenant_id = @TenantId AND timestamp BETWEEN @From AND @To 
                AND event_type = 'PluginCall' AND content LIKE '%BookAppointment%'"
            : @"SELECT COUNT(DISTINCT session_id) FROM audits 
                WHERE timestamp BETWEEN @From AND @To 
                AND event_type = 'PluginCall' AND content LIKE '%BookAppointment%'";
        var sessionsWithBooking = await connection.QuerySingleAsync<int>(sessionsWithBookingSql, new { TenantId = tenantId, From = from, To = to });

        var conversionRate = uniqueSessions > 0 ? Math.Round((double)sessionsWithBooking / uniqueSessions * 100, 2) : 0;

        // Average time to booking (seconds from first message to booking event per session)
        var avgTimeSql = tenantId != null
            ? @"SELECT AVG(EXTRACT(EPOCH FROM (booking_msg - first_msg))) FROM (
                    SELECT a.session_id,
                           MIN(CASE WHEN a.event_type LIKE '%UserMessage' THEN a.timestamp END) as first_msg,
                           MIN(CASE WHEN a.event_type = 'PluginCall' AND a.content LIKE '%BookAppointment%' THEN a.timestamp END) as booking_msg
                    FROM audits a
                    WHERE a.tenant_id = @TenantId AND a.timestamp BETWEEN @From AND @To
                    GROUP BY a.session_id
                    HAVING MIN(CASE WHEN a.event_type = 'PluginCall' AND a.content LIKE '%BookAppointment%' THEN a.timestamp END) IS NOT NULL
                ) sub"
            : @"SELECT AVG(EXTRACT(EPOCH FROM (booking_msg - first_msg))) FROM (
                    SELECT a.session_id,
                           MIN(CASE WHEN a.event_type LIKE '%UserMessage' THEN a.timestamp END) as first_msg,
                           MIN(CASE WHEN a.event_type = 'PluginCall' AND a.content LIKE '%BookAppointment%' THEN a.timestamp END) as booking_msg
                    FROM audits a
                    WHERE a.timestamp BETWEEN @From AND @To
                    GROUP BY a.session_id
                    HAVING MIN(CASE WHEN a.event_type = 'PluginCall' AND a.content LIKE '%BookAppointment%' THEN a.timestamp END) IS NOT NULL
                ) sub";
        var avgTimeToBooking = await connection.QuerySingleOrDefaultAsync<double?>(avgTimeSql, new { TenantId = tenantId, From = from, To = to }) ?? 0;

        return new ReceptionistAgent.Core.Models.MetricsSummary
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
