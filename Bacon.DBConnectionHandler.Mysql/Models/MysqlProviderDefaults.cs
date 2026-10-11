namespace Bacon.DBConnectionHandler.Mysql.Models;

/// <summary>
/// Default values related to the MySQL provider
/// </summary>
public static class MysqlProviderDefaults
{
    /// <summary>
    /// The provider id of the MySQL connections, used by <see cref="Bacon.DBConnectionHandler.Models.ConnectionRecord.Provider"/>
    /// </summary>
    public const string Provider = "mysql";

    /// <summary>
    /// The default health check name of a MySQL connection (suffixed with the connection name for a named connection)
    /// </summary>
    public static readonly string HealthCheckName = "mysql";

    /// <summary>
    /// The default health check query of an MySQL connection
    /// </summary>
    public static readonly string HealthCheckQuery = "SELECT 1;";

    /// <summary>
    /// The default health check tag of an MySQL connection
    /// </summary>
    public static readonly string HealthCheckTag = "database";
}