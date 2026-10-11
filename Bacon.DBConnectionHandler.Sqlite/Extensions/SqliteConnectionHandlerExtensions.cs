using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Bacon.DBConnectionHandler.Sqlite.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Bacon.DBConnectionHandler.Sqlite.Extensions;

/// <summary>
/// SQLite provider extensions
/// </summary>
public static class SqliteConnectionHandlerExtensions
{
    #region Public

    /// <summary>
    /// Adds SQLite support, so SQLite connections can be added at runtime through <see cref="Bacon.DBConnectionHandler.Interfaces.Services.IConnectionHandlerManager"/>.
    /// It does not add a connection. Not needed if <c>AddSqlite</c> is called, it registers the support too
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddSqliteProvider(this ConnectionHandlerBuilder connectionHandlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        return connectionHandlerBuilder.AddProvider<SqliteConnectionProvider>();
    }

    /// <summary>Adds the default (empty name) SQLite connection</summary>
    public static ConnectionHandlerBuilder AddSqlite(this ConnectionHandlerBuilder connectionHandlerBuilder, Action<SqliteConnectionHandlerOptions> configureOptions)
    {
        return connectionHandlerBuilder.AddSqlite(string.Empty, configureOptions);
    }

    /// <summary>Adds a named SQLite connection</summary>
    /// <exception cref="ArgumentException">The options are invalid</exception>
    /// <exception cref="InvalidOperationException">The name is already used or a second default connection was added</exception>
    public static ConnectionHandlerBuilder AddSqlite(this ConnectionHandlerBuilder connectionHandlerBuilder, string name, Action<SqliteConnectionHandlerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        SqliteConnectionHandlerOptions sqliteConnectionHandlerOptions = new();
        configureOptions(sqliteConnectionHandlerOptions);
        sqliteConnectionHandlerOptions.Validate();

        return connectionHandlerBuilder.Add(
            new(
                    new ConnectionRecord(name, SqliteProviderDefaults.Provider, sqliteConnectionHandlerOptions),
                    Create: sp => new SqliteConnectionHandlerService(sqliteConnectionHandlerOptions, sp.GetRequiredService<ILogger<SqliteConnectionHandlerService>>()),
                    Test: () =>
                    {
                        using SqliteConnection sqliteConnection = new(sqliteConnectionHandlerOptions.ConnectionString);
                        sqliteConnection.Open();
                    },
                    ConfigureServices: services => AddHealthCheck(services, name, sqliteConnectionHandlerOptions))
            ).AddSqliteProvider();
    }

    #endregion Public

    #region Private

    private static void AddHealthCheck(IServiceCollection services, string name, SqliteConnectionHandlerOptions sqliteConnectionHandlerOptions)
    {
        if (sqliteConnectionHandlerOptions.ConnectionHandlerHealthCheckOptions is not { } healthCheck)
        {
            return;
        }

        services.AddHealthChecks()
            .AddSqlite(
                sqliteConnectionHandlerOptions.ConnectionString,
                string.IsNullOrWhiteSpace(healthCheck.HealthQuery) ? SqliteProviderDefaults.HealthCheckQuery : healthCheck.HealthQuery,
                null,
                healthCheck.ResolveName(name, SqliteProviderDefaults.HealthCheckName),
                healthCheck.FailureStatus ?? HealthStatus.Unhealthy,
                healthCheck.Tags ?? [SqliteProviderDefaults.HealthCheckTag],
                healthCheck.Timeout);
    }

    #endregion Private
}