using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Bacon.DBConnectionHandler.Registries;

internal sealed partial class DynamicConnectionRegistry (IConnectionStore connectionStore, IEnumerable<IConnectionProvider> connectionProviders, IServiceProvider serviceProvider, DynamicConnectionOptions dynamicConnectionOptions
    , TimeProvider timeProvider, ILogger<DynamicConnectionRegistry> logger) : IAsyncDisposable, IDisposable
{
    private sealed record Materialized(IConnectionHandlerService Service, long CreatedTimestamp);

    // Lazy<Task> so concurrent first calls for the same name share one load
    private readonly ConcurrentDictionary<string, Lazy<Task<Materialized?>>> _entries = [];
    private readonly FrozenDictionary<string, IConnectionProvider> _providers = connectionProviders.ToFrozenDictionary(p => p.Provider, StringComparer.OrdinalIgnoreCase);

    #region Read

    /// <summary>
    /// Names of the connections currently materialized on this node
    /// </summary>
    public IReadOnlyCollection<string> MaterializedNames
    {
        get
        {
            List<string> names = [];

            foreach (KeyValuePair<string, Lazy<Task<Materialized?>>> entry in _entries)
            {
                if (TryGetCompleted(entry.Value, out Materialized? materialized) && materialized is not null)
                {
                    names.Add(entry.Key);
                }
            }

            return names;
        }
    }

    public IConnectionProvider? FindProvider(string provider)
    {
        _providers.TryGetValue(provider, out IConnectionProvider? connectionProvider);

        return connectionProvider;
    }

    /// <summary>
    /// Gets the service, loading it from the store on a miss
    /// </summary>
    /// <returns>The service, or null if the store has no connection with this name</returns>
    public ValueTask<IConnectionHandlerService?> GetAsync(string name, CancellationToken cancellationToken)
    {
        // Hot path: already loaded and not expired, no allocation and no await
        if (_entries.TryGetValue(name, out Lazy<Task<Materialized?>>? lazy) && TryGetCompleted(lazy, out Materialized? materialized) && materialized is not null && !IsExpired(materialized))
        {
            return ValueTask.FromResult<IConnectionHandlerService?>(materialized.Service);
        }

        return LoadAsync(name, cancellationToken);
    }

    #endregion Read

    #region Write

    /// <summary>
    /// Drops the materialized entry, if any. The next call rebuilds it from the store
    /// </summary>
    public void Invalidate(string name)
    {
        if (_entries.TryRemove(name, out Lazy<Task<Materialized?>>? lazy))
        {
            RetireEntry(lazy);
        }
    }

    #endregion Write

    #region Private

    #region Load

    private async ValueTask<IConnectionHandlerService?> LoadAsync(string name, CancellationToken cancellationToken)
    {
        while (true)
        {
            Lazy<Task<Materialized?>> lazy = _entries.GetOrAdd(name, key => new Lazy<Task<Materialized?>>(() => MaterializeAsync(key)));
            Materialized? materialized;

            try
            {
                // The load itself ignores the caller's token: one caller cancelling must not fail the others sharing the load
                materialized = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A failed load is never cached, so the next call retries
                RemoveEntry(name, lazy);
                throw;
            }

            if (materialized is null)
            {
                // Unknown name. Not cached either, so a connection added later is seen immediately
                RemoveEntry(name, lazy);

                return null;
            }

            if (IsExpired(materialized))
            {
                if (RemoveEntry(name, lazy))
                {
                    RetireEntry(lazy);
                }

                // Reload from the store
                continue;
            }

            return materialized.Service;
        }
    }

    private async Task<Materialized?> MaterializeAsync(string name)
    {
        ConnectionRecord? connectionRecord = await connectionStore.GetAsync(name, CancellationToken.None).ConfigureAwait(false);

        if (connectionRecord is null)
        {
            return null;
        }

        IConnectionProvider connectionProvider = FindProvider(connectionRecord.Provider) ?? throw new InvalidOperationException($"DB - No provider '{connectionRecord.Provider}' is registered for the connection '{name}'");

        long createdTimestamp = dynamicConnectionOptions.CacheDuration is null ? 0 : timeProvider.GetTimestamp();

        return new Materialized(connectionProvider.Create(connectionRecord, serviceProvider), createdTimestamp);
    }

    #endregion Load

    #region Remove

    // Only removes the exact entry, so a concurrent replacement is never dropped by mistake
    private bool RemoveEntry(string name, Lazy<Task<Materialized?>> lazy)
    {
        return _entries.TryRemove(new KeyValuePair<string, Lazy<Task<Materialized?>>>(name, lazy));
    }

    // Retires the service once its load is done, so a load that was still running when it was dropped does not leak
    private void RetireEntry(Lazy<Task<Materialized?>> lazy)
    {
        if (!lazy.IsValueCreated)
        {
            return;
        }

        Task<Materialized?> load = lazy.Value;

        // Common case: the load finished long ago, no continuation needed
        if (load.IsCompleted)
        {
            if (load.IsCompletedSuccessfully && load.Result is { } materialized)
            {
                Retire(materialized.Service);
            }

            return;
        }

        // The load is still running: retire what it builds once it is done, so it does not leak
        _ = load.ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully && task.Result is { } materialized)
                {
                    Retire(materialized.Service);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Calls already running keep the old instance, so disposal is delayed. Services that own nothing are just dropped
    private void Retire(IConnectionHandlerService service)
    {
        if (service is not (IAsyncDisposable or IDisposable))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (dynamicConnectionOptions.RetireDelay > TimeSpan.Zero)
                {
                    await Task.Delay(dynamicConnectionOptions.RetireDelay, timeProvider).ConfigureAwait(false);
                }

                await DisposeServiceAsync(service).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRetireException(logger, ex);
            }
        });
    }

    #endregion Remove

    private bool IsExpired(Materialized materialized)
    {
        return dynamicConnectionOptions.CacheDuration is { } cacheDuration && timeProvider.GetElapsedTime(materialized.CreatedTimestamp) >= cacheDuration;
    }

    private static bool TryGetCompleted(Lazy<Task<Materialized?>> lazy, out Materialized? materialized)
    {
        materialized = null;

        if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            materialized = lazy.Value.Result;

            return true;
        }

        return false;
    }

    #endregion Private

    #region Dispose

    // Shutdown: dispose what is still alive, without the delay
    public async ValueTask DisposeAsync()
    {
        foreach (KeyValuePair<string, Lazy<Task<Materialized?>>> entry in _entries)
        {
            if (TryGetCompleted(entry.Value, out Materialized? materialized) && materialized is not null)
            {
                try
                {
                    await DisposeServiceAsync(materialized.Service).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogRetireException(logger, ex);
                }
            }
        }

        _entries.Clear();
    }

    // Lets a provider disposed with a plain Dispose() work. It only waits for services that are really async,
    // a sync one completes immediately. ConfigureAwait(false) above prevents a deadlock on a thread with a SynchronizationContext
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static async ValueTask DisposeServiceAsync(IConnectionHandlerService service)
    {
        // Async first, so nothing blocks when the service supports it. A service can implement either kind, or both
        if (service is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (service is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    #endregion Dispose

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - Failed to dispose a retired dynamic connection")]
    private static partial void LogRetireException(ILogger logger, Exception exception);

    #endregion Logs
}