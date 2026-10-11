namespace Bacon.DBConnectionHandler.Sqlite.Models;

/// <summary>
/// Default values related to the SQLite provider
/// </summary>
public static class SqliteProviderDefaults
{
    /// <summary>
    /// The provider id of the SQLite connections, used by <see cref="Bacon.DBConnectionHandler.Models.ConnectionRecord.Provider"/>
    /// </summary>
    public const string Provider = "sqlite";

    /// <summary>
    /// The default health check name of an SQLite connection (suffixed with the connection name for a named connection)
    /// </summary>
    public static readonly string HealthCheckName = "sqlite";

    /// <summary>
    /// The default health check query of an SQLite connection
    /// </summary>
    public static readonly string HealthCheckQuery = "SELECT 1;";

    /// <summary>
    /// The default health check tag of an SQLite connection
    /// </summary>
    public static readonly string HealthCheckTag = "database";
}