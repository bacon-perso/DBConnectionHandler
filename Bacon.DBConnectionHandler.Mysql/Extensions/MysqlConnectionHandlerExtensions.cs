using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Mysql.Services;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Bacon.DBConnectionHandler.Mysql.Extensions;

/// <summary>
/// MySQL provider extensions
/// </summary>
public static class MysqlConnectionHandlerExtensions
{
    #region Public

    /// <summary>
    /// Adds MySQL support, so MySQL connections can be added at runtime through <see cref="Bacon.DBConnectionHandler.Interfaces.Services.IConnectionHandlerManager"/>.
    /// It does not add a connection. Not needed if <c>AddMysql</c> is called, it registers the support too
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddMysqlProvider(this ConnectionHandlerBuilder connectionHandlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        return connectionHandlerBuilder.AddProvider<MysqlConnectionProvider>();
    }

    /// <summary>
    /// Adds the default (empty name) MySQL connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="configureOptions">The MySQL connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddMysql(this ConnectionHandlerBuilder connectionHandlerBuilder, Action<MysqlConnectionHandlerOptions> configureOptions)
    {
        return connectionHandlerBuilder.AddMysql(string.Empty, configureOptions);
    }

    /// <summary>
    /// Adds a named MySQL connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="name">The connection name. The empty string is the default connection</param>
    /// <param name="configureOptions">The MySQL connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    /// <exception cref="ArgumentException">The options are invalid</exception>
    /// <exception cref="InvalidOperationException">The name is already used or a second default connection was added</exception>
    public static ConnectionHandlerBuilder AddMysql(this ConnectionHandlerBuilder connectionHandlerBuilder, string name, Action<MysqlConnectionHandlerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        MysqlConnectionHandlerOptions mysqlConnectionHandlerOptions = new();
        configureOptions(mysqlConnectionHandlerOptions);
        mysqlConnectionHandlerOptions.Validate();

        return connectionHandlerBuilder.Add(
            new(
                    new ConnectionRecord(name, MysqlProviderDefaults.Provider, mysqlConnectionHandlerOptions),
                    Create: sp => new MysqlConnectionHandlerService(mysqlConnectionHandlerOptions, sp.GetRequiredService<ILogger<MysqlConnectionHandlerService>>()),
                    Test: () =>
                    {
                        using MySqlConnection mySqlConnection = new(mysqlConnectionHandlerOptions.ConnectionString);
                        mySqlConnection.Open();
                    },
                    ConfigureServices: services => AddHealthCheck(services, name, mysqlConnectionHandlerOptions))
            ).AddMysqlProvider();
    }

    #endregion Public

    #region Private

    private static void AddHealthCheck(IServiceCollection services, string name, MysqlConnectionHandlerOptions mysqlConnectionHandlerOptions)
    {
        if (mysqlConnectionHandlerOptions.ConnectionHandlerHealthCheckOptions is not { } healthCheck)
        {
            return;
        }

        services.AddHealthChecks()
            .AddMySql(
                mysqlConnectionHandlerOptions.ConnectionString,
                string.IsNullOrWhiteSpace(healthCheck.HealthQuery) ? MysqlProviderDefaults.HealthCheckQuery : healthCheck.HealthQuery,
                null,
                healthCheck.ResolveName(name, MysqlProviderDefaults.HealthCheckName),
                healthCheck.FailureStatus ?? HealthStatus.Unhealthy,
                healthCheck.Tags ?? [MysqlProviderDefaults.HealthCheckTag],
                healthCheck.Timeout);
    }

    #endregion Private
}