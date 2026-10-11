using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;

namespace Bacon.DBConnectionHandler.Extensions;

/// <summary>
/// Adds connections to the builder and builds the registration
/// </summary>
public static class ConnectionHandlerBuilderExtensions
{
    #region Public

    #region Add

    /// <summary>
    /// Registers a provider so it can create dynamic connections. Meant to be called by the provider extension methods
    /// </summary>
    /// <typeparam name="TProvider">The provider implementation</typeparam>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The builder, for chaining</returns>
    public static ConnectionHandlerBuilder AddProvider<TProvider>(this ConnectionHandlerBuilder connectionHandlerBuilder) where TProvider : class, IConnectionProvider
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        // TryAddEnumerable makes a repeated call (e.g. AddMssql then AddMssqlProvider) harmless
        connectionHandlerBuilder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionProvider, TProvider>());
        connectionHandlerBuilder.HasProviders = true;

        return connectionHandlerBuilder;
    }

    /// <summary>
    /// Adds a connection. Meant to be called by the provider extension methods
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <param name="connectionHandle">The connection to add</param>
    /// <returns>The builder, for chaining</returns>
    /// <exception cref="InvalidOperationException">The name is already used, or a second default connection was added</exception>
    public static ConnectionHandlerBuilder Add(this ConnectionHandlerBuilder connectionHandlerBuilder, ConnectionHandle connectionHandle)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);
        ArgumentNullException.ThrowIfNull(connectionHandle);

        // Fail at the offending line, not later in Build()
        if (connectionHandlerBuilder.ConnectionHandles.Any(a => a.Record.Name == connectionHandle.Record.Name))
        {
            throw new InvalidOperationException(connectionHandle.Record.Name.Length == 0 ? "DB - Only one default (empty name) connection can be registered" : $"DB - A connection named '{connectionHandle.Record.Name}' is already registered");
        }

        connectionHandlerBuilder.ConnectionHandles.Add(connectionHandle);

        return connectionHandlerBuilder;
    }

    #endregion Add

    #region Build

    /// <summary>
    /// Tests every connection (opens each one once), then registers them.
    /// Nothing is added to the service collection if a connection cannot be opened.
    /// </summary>
    /// <param name="connectionHandlerBuilder">The connection handler builder</param>
    /// <returns>The service collection</returns>
    /// <exception cref="InvalidOperationException">No connection was added and no provider was registered</exception>
    /// <exception cref="AggregateException">One InvalidOperationException per connection that cannot be opened, each wrapping the database exception</exception>
    public static IServiceCollection Build(this ConnectionHandlerBuilder connectionHandlerBuilder)
    {
        ArgumentNullException.ThrowIfNull(connectionHandlerBuilder);

        if (connectionHandlerBuilder.ConnectionHandles.Count == 0 && !connectionHandlerBuilder.HasProviders)
        {
            throw new InvalidOperationException("DB - AddConnectionHandler() was called but no provider was added (e.g. AddMssql or AddMssqlProvider)");
        }

        ConcurrentDictionary<string, Exception> exceptions = [];
        // Test every connection first, so nothing is registered if a database is unreachable
        Parallel.ForEach(connectionHandlerBuilder.ConnectionHandles, handle =>
        {
            try
            {
                handle.Test();
            }
            catch (Exception ex)
            {
                exceptions.TryAdd(handle.Record.Name, new InvalidOperationException($"DB - Could not connect to the {handle.Record.Provider} connection '{GetName(handle.Record.Name)}'", ex));
            }
        });

        if (!exceptions.IsEmpty)
        {
            // Ordered by name so the output is stable even though the tests finish in any order
            string names = string.Join(", ", exceptions.Keys.Order().Select(GetName));

            throw new AggregateException($"DB - Could not connect to: {names}", exceptions.OrderBy(f => f.Key).Select(f => f.Value));
        }

        // Register every connection
        foreach (ConnectionHandle connectionHandle in connectionHandlerBuilder.ConnectionHandles)
        {
            connectionHandlerBuilder.Registry.Add(connectionHandle.Record);

            connectionHandlerBuilder.Services.TryAddKeyedSingleton(connectionHandle.Record.Name, (sp, _) => connectionHandle.Create(sp));

            //Add healthchecks if not null
            connectionHandle.ConfigureServices?.Invoke(connectionHandlerBuilder.Services);
        }

        // The default connection is also injectable as a plain IConnectionHandlerService
        if (connectionHandlerBuilder.ConnectionHandles.Any(a => a.Record.Name.Length == 0))
        {
            connectionHandlerBuilder.Services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IConnectionHandlerService>(string.Empty));
        }

        connectionHandlerBuilder.Registry.MarkBuilt();

        return connectionHandlerBuilder.Services;
    }

    #endregion Build

    #endregion Public

    #region Private

    private static string GetName(string name) => name.Length == 0 ? "default" : name;

    #endregion Private
}