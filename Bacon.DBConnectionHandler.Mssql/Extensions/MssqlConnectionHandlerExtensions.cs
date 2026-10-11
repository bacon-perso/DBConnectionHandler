using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mssql.Services;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Bacon.DBConnectionHandler.Mssql.Extensions;

/// <summary>
/// MSSQL provider extensions
/// </summary>
public static class MssqlConnectionHandlerExtensions
{
    #region Public

    /// <summary>
    /// Adds MSSQL support, so MSSQL connections can be added at runtime through <see cref="Bacon.DBConnectionHandler.Interfaces.Services.IConnectionHandlerManager"/>.
    /// It does not add a connection. Not needed if <c>AddMssql</c> is called, it registers the support too
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddMssqlProvider(this ConnectionHandlerBuilder connectionHandlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        return connectionHandlerBuilder.AddProvider<MssqlConnectionProvider>();
    }

    /// <summary>
    /// Adds the default (empty name) MSSQL connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="configureOptions">The MSSQL connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddMssql(this ConnectionHandlerBuilder connectionHandlerBuilder, Action<MssqlConnectionHandlerOptions> configureOptions)
    {
        return connectionHandlerBuilder.AddMssql(string.Empty, configureOptions);
    }

    /// <summary>
    /// Adds a named MSSQL connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="name">The connection name. The empty string is the default connection</param>
    /// <param name="configureOptions">The MSSQL connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    /// <exception cref="ArgumentException">The options are invalid</exception>
    /// <exception cref="InvalidOperationException">The name is already used or a second default connection was added</exception>
    public static ConnectionHandlerBuilder AddMssql(this ConnectionHandlerBuilder connectionHandlerBuilder, string name, Action<MssqlConnectionHandlerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        MssqlConnectionHandlerOptions mssqlConnectionHandlerOptions = new();
        configureOptions(mssqlConnectionHandlerOptions);
        mssqlConnectionHandlerOptions.Validate();

        return connectionHandlerBuilder.Add(
            new (
                    new ConnectionRecord(name, MssqlProviderDefaults.Provider, mssqlConnectionHandlerOptions),
                    Create: sp => new MssqlConnectionHandlerService(mssqlConnectionHandlerOptions, UseAsyncRead(), sp.GetRequiredService<ILogger<MssqlConnectionHandlerService>>()),
                    Test: () =>
                    {
                        using SqlConnection sqlConnection = new(mssqlConnectionHandlerOptions.ConnectionString);
                        sqlConnection.Open();
                    },
                    ConfigureServices: services => AddHealthCheck(services, name, mssqlConnectionHandlerOptions))
            ).AddMssqlProvider();
    }

    #endregion Public

    #region Private

    private static void AddHealthCheck(IServiceCollection services, string name, MssqlConnectionHandlerOptions mssqlConnectionHandlerOptions)
    {
        if (mssqlConnectionHandlerOptions.ConnectionHandlerHealthCheckOptions is not { } healthCheck)
        {
            return;
        }

        services.AddHealthChecks()
            .AddSqlServer(
                mssqlConnectionHandlerOptions.ConnectionString,
                string.IsNullOrWhiteSpace(healthCheck.HealthQuery) ? MssqlProviderDefaults.HealthCheckQuery : healthCheck.HealthQuery,
                null,
                healthCheck.ResolveName(name, MssqlProviderDefaults.HealthCheckName),
                healthCheck.FailureStatus ?? HealthStatus.Unhealthy,
                healthCheck.Tags ?? [MssqlProviderDefaults.HealthCheckTag],
                healthCheck.Timeout);
    }

    internal static bool UseAsyncRead()
    {
         // Opt-in switches that allow packet multiplexing, a preview feature. Both must be set to false.
        /*
        Sources:
        - AppContext switches in SqlClient — Microsoft Learn (https://learn.microsoft.com/en-us/sql/connect/ado-net/appcontext-switches?view=sql-server-ver17)
        - SqlClient 7.0 release notes (https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md)
        - Get a 180% speed increase on large async reads — ErikEJ's blog (https://erikej.github.io/sqlclient/dotnet/performance/2025/10/20/sqlclient-performance.html)
        */
        return AppContext.TryGetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityAsyncBehaviour", out bool asyncValue) && !asyncValue &&
            AppContext.TryGetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityProcessSni", out bool sniValue) && !sniValue;
    }

    #endregion Private
}