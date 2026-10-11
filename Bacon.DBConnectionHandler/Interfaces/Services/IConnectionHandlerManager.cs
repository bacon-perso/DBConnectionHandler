using Bacon.DBConnectionHandler.Models;

namespace Bacon.DBConnectionHandler.Interfaces.Services;

/// <summary>
/// Adds, updates and removes dynamic (e.g. tenant) connections at runtime.
/// Static connections registered in code are read-only here.
/// Changes are local to the store: with the default in-memory store they last until restart.
/// Failures of the database are reported in the result. Failures of the store are thrown
/// </summary>
public interface IConnectionHandlerManager
{
    /// <summary>
    /// Gets the names of the dynamic connections. Complete if the store is listable, otherwise only the ones loaded on this node
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a connection. The database is opened first, nothing is stored if it cannot be
    /// </summary>
    ValueTask<ConnectionChangeResult> AddAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a dynamic connection. The database is opened first, the previous definition is kept if it cannot be.
    /// The new definition is used from the next call, the previous service is retired after <see cref="Models.Options.DynamicConnectionOptions.RetireDelay"/>
    /// </summary>
    ValueTask<ConnectionChangeResult> UpdateAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a dynamic connection
    /// </summary>
    ValueTask<ConnectionChangeResult> RemoveAsync(string name, CancellationToken cancellationToken = default);
}