namespace Bacon.DBConnectionHandler.Postgres.Models;

/// <summary>
/// Default values related to the Postgres provider
/// </summary>
public static class PostgresProviderDefaults
{
    /// <summary>
    /// The provider id of the Postgres connections, used by <see cref="Bacon.DBConnectionHandler.Models.ConnectionRecord.Provider"/>
    /// </summary>
    public const string Provider = "postgres";

    /// <summary>
    /// The default health check name of an Postgres connection (suffixed with the connection name for a named connection)
    /// </summary>
    public static readonly string HealthCheckName = "postgres";

    /// <summary>
    /// The default health check query of an Postgres connection
    /// </summary>
    public static readonly string HealthCheckQuery = "SELECT 1;";

    /// <summary>
    /// The default health check tag of an Postgres connection
    /// </summary>
    public static readonly string HealthCheckTag = "database";
}