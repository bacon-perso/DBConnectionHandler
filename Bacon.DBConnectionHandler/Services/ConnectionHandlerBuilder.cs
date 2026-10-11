using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Services;

/// <summary>
/// Holds the connections collected while registering.
/// The behavior lives in <see cref="Extensions.ConnectionHandlerBuilderExtensions"/>
/// </summary>
public sealed class ConnectionHandlerBuilder
{
    /// <summary>
    /// The service collection being configured
    /// </summary>
    public IServiceCollection Services { get; }

    internal ConnectionHandlerRegistry Registry { get; }
    internal List<ConnectionHandle> ConnectionHandles { get; } = [];
    internal DynamicConnectionOptions DynamicOptions { get; }
    internal bool HasProviders { get; set; } // True once a provider is registered, so Build() accepts an app that only uses dynamic connections

    internal ConnectionHandlerBuilder(IServiceCollection services, ConnectionHandlerRegistry connectionHandlerRegistry, DynamicConnectionOptions dynamicConnectionOptions)
    {
        Services = services;
        Registry = connectionHandlerRegistry;
        DynamicOptions = dynamicConnectionOptions;
    }
}