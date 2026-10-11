using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bacon.DBConnectionHandler.Models.Options;

/// <summary>
/// Defines the ConnectionHandler Health check configurations
/// </summary>
public class ConnectionHandlerHealthCheckOptions
{
    /// <summary>
    /// Defines the unique name of the health check. (default value : null, the provider's default name is used, e.g. "sqlserver" for MSSQL,
    /// suffixed with ":&lt;connection name&gt;" for a named connection. See <see cref="ResolveName"/>)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Defines the status used if the check fails. (default value : null, <see cref="HealthStatus.Unhealthy"/> is used)
    /// </summary>
    public HealthStatus? FailureStatus { get; set; }

    /// <summary>
    /// Defines the SQL query used to ping the server. (default value : null, the provider's default query is used, "SELECT 1;" for every provider today)
    /// </summary>
    public string? HealthQuery { get; set; }

    /// <summary>
    /// Defines the time before the check is considered timed out. (default value : null, the health check's own default applies)
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Defines the tags. (default value : null, the provider's default tag is used, ["database"])
    /// </summary>
    public IEnumerable<string>? Tags { get; set; }

    /// <summary>
    /// Resolves the health check name. An explicit Name always wins. Otherwise the provider default is used,
    /// suffixed with the connection name for named connections so names stay unique.
    /// </summary>
    /// <param name="connectionName">The connection name ("" for the default connection)</param>
    /// <param name="providerDefault">The provider default name, taken from the provider's defaults class (e.g. "sqlserver" from MssqlProviderDefaults.HealthCheckName)</param>
    public string ResolveName(string connectionName, string providerDefault) => !string.IsNullOrWhiteSpace(Name) ? Name : connectionName.Length == 0 ? providerDefault : $"{providerDefault}:{connectionName}";
}