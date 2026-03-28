using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Repositories;
using ReceptionistAgent.Core.Tenant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReceptionistAgent.Connectors.Repositories;

/// <summary>
/// Implementación unificada para operaciones administrativas en bases de datos de tenants.
/// Soporta SQL Server y PostgreSQL dinámicamente.
/// </summary>
public class DatabaseAdminRepository : IDatabaseAdminRepository
{
    public async Task<List<TableInfo>> GetTablesAsync(TenantConfiguration tenant)
    {
        if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<TableInfo>(
                @"SELECT TABLE_NAME as TableName, TABLE_TYPE as TableType 
                  FROM INFORMATION_SCHEMA.TABLES 
                  WHERE TABLE_TYPE = 'BASE TABLE' 
                  ORDER BY TABLE_NAME"
            )).ToList();
        }
        else if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<TableInfo>(
                @"SELECT table_name as TableName, table_type as TableType 
                  FROM information_schema.tables 
                  WHERE table_schema = 'public' AND table_type = 'BASE TABLE' 
                  ORDER BY table_name"
            )).ToList();
        }
        return new List<TableInfo>();
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(TenantConfiguration tenant, string tableName)
    {
        if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<ColumnInfo>(
                @"SELECT 
                    COLUMN_NAME as ColumnName,
                    DATA_TYPE as DataType,
                    IS_NULLABLE as IsNullable,
                    CHARACTER_MAXIMUM_LENGTH as MaxLength,
                    COLUMN_DEFAULT as DefaultValue,
                    ORDINAL_POSITION as OrdinalPosition
                  FROM INFORMATION_SCHEMA.COLUMNS 
                  WHERE TABLE_NAME = @TableName
                  ORDER BY ORDINAL_POSITION",
                new { TableName = tableName }
            )).ToList();
        }
        else if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<ColumnInfo>(
                @"SELECT 
                    column_name as ColumnName,
                    data_type as DataType,
                    is_nullable as IsNullable,
                    character_maximum_length as MaxLength,
                    column_default as DefaultValue,
                    ordinal_position as OrdinalPosition
                  FROM information_schema.columns 
                  WHERE table_name = @TableName
                  ORDER BY ordinal_position",
                new { TableName = tableName.ToLower() }
            )).ToList();
        }
        return new List<ColumnInfo>();
    }

    public async Task<IEnumerable<dynamic>> GetRecentChatMessagesAsync(TenantConfiguration tenant, int limit = 50)
    {
        if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            return await connection.QueryAsync<dynamic>("SELECT * FROM chat_messages ORDER BY timestamp DESC LIMIT @Limit", new { Limit = limit });
        }
        else if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            return await connection.QueryAsync<dynamic>("SELECT TOP (@Limit) * FROM chat_messages ORDER BY [timestamp] DESC", new { Limit = limit });
        }
        return Enumerable.Empty<dynamic>();
    }

    public async Task<List<dynamic>> GetRawBookingsAsync(TenantConfiguration tenant, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<dynamic>("SELECT TOP (@Limit) * FROM Bookings ORDER BY CreatedAt DESC", new { Limit = limit })).ToList();
        }
        else if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            return (await connection.QueryAsync<dynamic>("SELECT * FROM bookings ORDER BY created_at DESC LIMIT @Limit", new { Limit = limit })).ToList();
        }
        return new List<dynamic>();
    }

    public async Task<int> GetTotalBookingsCountAsync(TenantConfiguration tenant)
    {
        if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Bookings WHERE IsDeleted = 0");
        }
        else if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM bookings WHERE is_deleted = false");
        }
        return 0;
    }

    public async Task<Dictionary<string, object>> GetDatabaseHealthAsync(TenantConfiguration tenant)
    {
        var health = new Dictionary<string, object>();
        var tablesToCheck = tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
            ? new[] { "providers", "chat_sessions", "reminders", "bookings", "tenant_billing", "chat_messages" }
            : new[] { "Providers", "ChatSessions", "Reminders", "Bookings", "chat_messages" };

        if (tenant.DbType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new SqlConnection(tenant.ConnectionString);
            await connection.OpenAsync();
            foreach (var table in tablesToCheck)
            {
                var existsSql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName";
                var exists = await connection.ExecuteScalarAsync<int>(existsSql, new { TableName = table }) > 0;
                int count = exists ? await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{table}]") : 0;
                health[table] = new { Exists = exists, RowCount = count };
            }
        }
        else if (tenant.DbType.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            using var connection = new NpgsqlConnection(tenant.ConnectionString);
            await connection.OpenAsync();
            foreach (var table in tablesToCheck)
            {
                var sql = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @TableName);";
                var exists = await connection.ExecuteScalarAsync<bool>(sql, new { TableName = table });
                int count = exists ? await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM \"{table}\"") : 0;
                health[table] = new { Exists = exists, RowCount = count };
            }
        }
        return health;
    }
}
