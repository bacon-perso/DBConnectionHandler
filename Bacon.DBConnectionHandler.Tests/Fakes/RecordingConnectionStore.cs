using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Stores;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A listable store over the in-memory one that counts reads and can be told to fail
/// </summary>
internal sealed class RecordingConnectionStore : IListableConnectionStore
{
    private readonly InMemoryConnectionHandlerStore _inner = new();
    private int _getCount;

    public int GetCount => Volatile.Read(ref _getCount);

    /// <summary>
    /// Thrown by <see cref="SetAsync"/> when set
    /// </summary>
    public Exception? SetFailure { get; set; }

    /// <summary>
    /// When set, reads wait for it. Lets a test keep a load in flight
    /// </summary>
    public Task? GetGate { get; set; }

    public async ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getCount);

        if (GetGate is not null)
        {
            await GetGate;
        }

        return await _inner.GetAsync(name, cancellationToken);
    }

    public ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        if (SetFailure is not null)
        {
            throw SetFailure;
        }

        return _inner.SetAsync(connectionRecord, cancellationToken);
    }

    public ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) => _inner.RemoveAsync(name, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default) => _inner.GetNamesAsync(cancellationToken);
}

/// <summary>
/// A store that cannot list its names, like many distributed caches
/// </summary>
internal sealed class NonListableConnectionStore : IConnectionStore
{
    private readonly InMemoryConnectionHandlerStore _inner = new();

    public ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default) => _inner.GetAsync(name, cancellationToken);

    public ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default) => _inner.SetAsync(connectionRecord, cancellationToken);

    public ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) => _inner.RemoveAsync(name, cancellationToken);
}
