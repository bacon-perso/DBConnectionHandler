namespace Bacon.DBConnectionHandler.Mssql.Models;

/// <summary>
/// Default values related to the MSSQL provider
/// </summary>
public static class MssqlProviderDefaults
{
    /// <summary>
    /// The provider id of the MSSQL connections, used by <see cref="Bacon.DBConnectionHandler.Models.ConnectionRecord.Provider"/>
    /// </summary>
    public const string Provider = "mssql";

    /// <summary>
    /// The default health check name of an MSSQL connection (suffixed with the connection name for a named connection)
    /// </summary>
    public static readonly string HealthCheckName = "sqlserver";

    /// <summary>
    /// The default health check query of an MSSQL connection
    /// </summary>
    public static readonly string HealthCheckQuery = "SELECT 1;";

    /// <summary>
    /// The default health check tag of an MSSQL connection
    /// </summary>
    public static readonly string HealthCheckTag = "database";
}