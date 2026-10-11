using Bacon.DBConnectionHandler.Models;

namespace Bacon.DBConnectionHandler.Interfaces.Stores;

/// <summary>
/// Holds the records of the dynamic connections. Static connections never go through it, so their secrets stay out of it.
/// Must be thread-safe. A shared implementation (distributed cache, tenant catalog) is responsible for encrypting
/// the connection strings it persists. Use <see cref="DBConnectionHandler.Services.BaseConnectionProvider{TOptions}.OptionsType"/> to deserialize the options
/// </summary>
public interface IConnectionStore
{
    /// <summary>
    /// Gets a connection record by name
    /// </summary>
    /// <returns>The connection record, or null if no dynamic connection has this name</returns>
    ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a connection record, or replaces the one that has the same name
    /// </summary>
    ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a connection record. Does nothing if no connection has this name
    /// </summary>
    /// <returns>True if it existed</returns>
    ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default);
}