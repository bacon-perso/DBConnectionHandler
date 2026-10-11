using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using System.Collections.Concurrent;

namespace Bacon.DBConnectionHandler.Stores;

internal sealed class InMemoryConnectionHandlerStore : IListableConnectionStore
{
    private readonly ConcurrentDictionary<string, ConnectionRecord> _records = [];

    public ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(name, out ConnectionRecord? connectionRecord);

        return ValueTask.FromResult(connectionRecord);
    }

    public ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<string> names = [.. _records.Keys];

        return ValueTask.FromResult(names);
    }

    public ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        _records[connectionRecord.Name] = connectionRecord;

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_records.TryRemove(name, out _));
    }
}