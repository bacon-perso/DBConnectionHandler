using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Postgres.Services;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bacon.DBConnectionHandler.Postgres.Extensions;

/// <summary>
/// Postgres provider extensions
/// </summary>
public static class PostgresConnectionHandlerExtensions
{
    #region Public

    /// <summary>
    /// Adds Postgres support, so Postgres connections can be added at runtime through <see cref="Bacon.DBConnectionHandler.Interfaces.Services.IConnectionHandlerManager"/>.
    /// It does not add a connection. Not needed if <c>AddPostgres</c> is called, it registers the support too
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddPostgresProvider(this ConnectionHandlerBuilder connectionHandlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        return connectionHandlerBuilder.AddProvider<PostgresConnectionProvider>();
    }

    /// <summary>
    /// Adds the default (empty name) Postgres connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="configureOptions">The Postgres connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddPostgres(this ConnectionHandlerBuilder connectionHandlerBuilder, Action<PostgresConnectionHandlerOptions> configureOptions)
    {
        return connectionHandlerBuilder.AddPostgres(string.Empty, configureOptions);
    }

    /// <summary>
    /// Adds a named Postgres connection
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="name">The connection name. The empty string is the default connection</param>
    /// <param name="configureOptions">The Postgres connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    /// <exception cref="ArgumentException">The options are invalid</exception>
    /// <exception cref="InvalidOperationException">The name is already used or a second default connection was added</exception>
    public static ConnectionHandlerBuilder AddPostgres(this ConnectionHandlerBuilder connectionHandlerBuilder, string name, Action<PostgresConnectionHandlerOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(configureOptions);

        PostgresConnectionHandlerOptions postgresConnectionHandlerOptions = new();
        configureOptions(postgresConnectionHandlerOptions);
        postgresConnectionHandlerOptions.Validate();

        return connectionHandlerBuilder.Add(
            new (
                    new ConnectionRecord(name, PostgresProviderDefaults.Provider, postgresConnectionHandlerOptions),
                    Create: sp => new PostgresConnectionHandlerService(postgresConnectionHandlerOptions, sp.GetRequiredService<ILogger<PostgresConnectionHandlerService>>()),
                    Test: () =>
                    {
                        using NpgsqlConnection npgsqlConnection = new(postgresConnectionHandlerOptions.ConnectionString);
                        npgsqlConnection.Open();
                    },
                    ConfigureServices: services => AddHealthCheck(services, name, postgresConnectionHandlerOptions))
            ).AddPostgresProvider();
    }

    #endregion Public

    #region Private

    private static void AddHealthCheck(IServiceCollection services, string name, PostgresConnectionHandlerOptions postgresConnectionHandlerOptions)
    {
        if (postgresConnectionHandlerOptions.ConnectionHandlerHealthCheckOptions is not { } healthCheck)
        {
            return;
        }

        services.AddHealthChecks()
            .AddNpgSql(
                postgresConnectionHandlerOptions.ConnectionString,
                string.IsNullOrWhiteSpace(healthCheck.HealthQuery) ? PostgresProviderDefaults.HealthCheckQuery : healthCheck.HealthQuery,
                null,
                healthCheck.ResolveName(name, PostgresProviderDefaults.HealthCheckName),
                healthCheck.FailureStatus ?? HealthStatus.Unhealthy,
                healthCheck.Tags ?? [PostgresProviderDefaults.HealthCheckTag],
                healthCheck.Timeout);
    }

    #endregion Private
}