namespace Bacon.DBConnectionHandler.Interfaces.Services;

/// <summary>
/// Resolves connection handlers by name at runtime
/// </summary>
public interface IConnectionHandlerFactory
{
    /// <summary>
    /// The static connection names plus the dynamic ones loaded on this node. The default connection is the empty string.
    /// Use <see cref="IConnectionHandlerManager.GetNamesAsync"/> for the dynamic connections in the store
    /// </summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>
    /// Gets the handler for a static or dynamic connection, loading a dynamic one from the store if needed.
    /// Call it on each use and do not keep the result: a dynamic connection can be updated or removed at any time
    /// </summary>
    /// <exception cref="InvalidOperationException">The name is unknown</exception>
    ValueTask<IConnectionHandlerService> GetHandlerAsync(string name = "", CancellationToken cancellationToken = default);
}