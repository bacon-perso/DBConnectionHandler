using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Registries;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bacon.DBConnectionHandler.Extensions;

/// <summary>
/// Connection handler extensions, allowing the middleware to work
/// </summary>
public static class ConnectionHandlerExtensions
{
    /// <summary>
    /// Creates the middleware and setups the dependency injection for IConnectionHandlerService as a singleton
    /// </summary>
    /// <param name="services">The service collection</param>
    public static ConnectionHandlerBuilder AddConnectionHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ConnectionHandlerRegistry connectionHandlerRegistry = new();
        DynamicConnectionOptions dynamicConnectionOptions = new();

        services.TryAddSingleton(connectionHandlerRegistry);
        services.TryAddSingleton(dynamicConnectionOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConnectionStore, InMemoryConnectionHandlerStore>();

        // Dynamic side: free if never used, nothing is created until a dynamic connection is added or resolved
        services.TryAddSingleton<DynamicConnectionRegistry>();
        services.TryAddSingleton<IConnectionHandlerManager, ConnectionHandlerManager>();
        services.TryAddTransient<IConnectionHandlerFactory, ConnectionHandlerFactory>();

        return new ConnectionHandlerBuilder(services, connectionHandlerRegistry, dynamicConnectionOptions);
    }

    /// <summary>
    /// Replaces the default in-memory store of the dynamic connections (e.g. with one backed by a distributed cache or a tenant catalog),
    /// registered as a singleton. Its dependencies must be registered in the container
    /// </summary>
    /// <typeparam name="TStore">The store implementation</typeparam>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder UseStore<TStore>(this ConnectionHandlerBuilder connectionHandlerBuilder) where TStore : class, IConnectionStore
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        connectionHandlerBuilder.Services.RemoveAll<IConnectionStore>();
        connectionHandlerBuilder.Services.AddSingleton<IConnectionStore, TStore>();

        return connectionHandlerBuilder;
    }

    /// <summary>
    /// Configures the dynamic connections (cache duration, retire delay)
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="configureOptions">The dynamic connection configuration</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder ConfigureDynamicConnections(this ConnectionHandlerBuilder connectionHandlerBuilder, Action<DynamicConnectionOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(configureOptions);

        configureOptions(connectionHandlerBuilder.DynamicOptions);

        return connectionHandlerBuilder;
    }
}