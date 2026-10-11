using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Registries;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Services;

internal sealed class ConnectionHandlerFactory(IServiceProvider serviceProvider, ConnectionHandlerRegistry connectionHandlerRegistry, DynamicConnectionRegistry dynamicConnectionRegistry) : IConnectionHandlerFactory
{
    #region Get Names

    public IReadOnlyCollection<string> Names => [.. connectionHandlerRegistry.Names, .. dynamicConnectionRegistry.MaterializedNames];

    #endregion Get Names

    #region Get Handler

    public async ValueTask<IConnectionHandlerService> GetHandlerAsync(string name = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (connectionHandlerRegistry.Contains(name))
        {
            return serviceProvider.GetRequiredKeyedService<IConnectionHandlerService>(name);
        }

        // The empty name is the static default, it is never looked up in the store
        IConnectionHandlerService? service = name.Length == 0 ? null : await dynamicConnectionRegistry.GetAsync(name, cancellationToken).ConfigureAwait(false);

        return service ?? throw CreateUnknownException(name);
    }

    #endregion Get Handler

    #region Private

    private InvalidOperationException CreateUnknownException(string name)
    {
        // Build() marks the registry, so an unbuilt registry almost always means Build() was never called
        if (!connectionHandlerRegistry.IsBuilt)
        {
            return new InvalidOperationException("DB - No connection is registered. Either no connection was added or .Build() was not called (eg : AddConnectionHandler().AddSomeProvider().Build()?");
        }

        return new InvalidOperationException($"DB - Unknown connection '{name}'.");
    }

    #endregion Private
}