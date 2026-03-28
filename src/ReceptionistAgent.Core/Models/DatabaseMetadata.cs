namespace ReceptionistAgent.Core.Models;

/// <summary>
/// Información sobre una tabla en la base de datos.
/// </summary>
public record TableInfo(string TableName, string TableType);

/// <summary>
/// Información sobre una columna de una tabla.
/// </summary>
public record ColumnInfo(
    string ColumnName, 
    string DataType, 
    string IsNullable,
    int? MaxLength, 
    string? DefaultValue, 
    int OrdinalPosition
);
