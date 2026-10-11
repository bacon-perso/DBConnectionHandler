using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models.Options;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A service that does nothing but remember how it was created. The subclasses differ only by how they can be disposed
/// </summary>
internal abstract class FakeConnectionHandlerServiceBase(string connectionString) : IConnectionHandlerService
{
    public string ConnectionString { get; } = connectionString;

    public DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null) => throw new NotSupportedException();

    public Task<IEnumerable<DataTable>> ExecuteAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ExecuteNoReturnAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Can only be disposed asynchronously
/// </summary>
internal sealed class FakeConnectionHandlerService(string connectionString) : FakeConnectionHandlerServiceBase(connectionString), IAsyncDisposable
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    /// <summary>
    /// Makes the disposal really asynchronous, so it does not complete before the caller starts waiting
    /// </summary>
    public TimeSpan DisposeDelay { get; init; } = TimeSpan.Zero;

    public async ValueTask DisposeAsync()
    {
        if (DisposeDelay > TimeSpan.Zero)
        {
            await Task.Delay(DisposeDelay).ConfigureAwait(false);
        }

        Interlocked.Increment(ref _disposeCount);
    }
}

/// <summary>
/// Can only be disposed synchronously
/// </summary>
internal sealed class SyncFakeConnectionHandlerService(string connectionString) : FakeConnectionHandlerServiceBase(connectionString), IDisposable
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
    }
}

/// <summary>
/// Supports both ways, and remembers which one was used
/// </summary>
internal sealed class BothFakeConnectionHandlerService(string connectionString) : FakeConnectionHandlerServiceBase(connectionString), IAsyncDisposable, IDisposable
{
    private int _asyncDisposeCount;
    private int _syncDisposeCount;

    public int AsyncDisposeCount => Volatile.Read(ref _asyncDisposeCount);

    public int SyncDisposeCount => Volatile.Read(ref _syncDisposeCount);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _asyncDisposeCount);

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _syncDisposeCount);
    }
}

/// <summary>
/// Owns nothing, like the services of the providers that pool by connection string
/// </summary>
internal sealed class PlainFakeConnectionHandlerService(string connectionString) : FakeConnectionHandlerServiceBase(connectionString)
{
}
