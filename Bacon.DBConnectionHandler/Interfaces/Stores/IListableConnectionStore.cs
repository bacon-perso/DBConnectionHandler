namespace Bacon.DBConnectionHandler.Interfaces.Stores;

/// <summary>
/// A store that can also list its names. Optional: many distributed stores cannot enumerate keys cheaply
/// </summary>
public interface IListableConnectionStore : IConnectionStore
{
    /// <summary>
    /// Gets the names of all the stored dynamic connections
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default);
}