using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Registries;

namespace Bacon.DBConnectionHandler.Services;

internal sealed class ConnectionHandlerManager(IConnectionStore connectionStore, DynamicConnectionRegistry dynamicConnectionRegistry, ConnectionHandlerRegistry connectionHandlerRegistry) : IConnectionHandlerManager, IDisposable
{
    // Changes are rare, so one lock is enough (no semaphore per tenant to leak). The slow database test runs outside of it
    private readonly SemaphoreSlim _mutationLock = new(1, 1); 

    #region Get

    public async ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default)
    {
        if (connectionStore is IListableConnectionStore listableConnectionStore)
        {
            return await listableConnectionStore.GetNamesAsync(cancellationToken).ConfigureAwait(false);
        }

        return dynamicConnectionRegistry.MaterializedNames;
    }

    #endregion Get

    #region Add

    public async ValueTask<ConnectionChangeResult> AddAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(connectionRecord);

        // A name used by a static connection is "taken", the empty name is reserved for the static default
        if (connectionHandlerRegistry.Contains(connectionRecord.Name))
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.AlreadyExists);
        }

        if (connectionRecord.Name.Length == 0)
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.NotAllowed);
        }

        ConnectionChangeResult? rejection = await PrepareAsync(connectionRecord, cancellationToken).ConfigureAwait(false);

        if (rejection is not null)
        {
            return rejection;
        }

        #endregion Validations

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await connectionStore.GetAsync(connectionRecord.Name, cancellationToken).ConfigureAwait(false) is not null)
            {
                return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.AlreadyExists);
            }

            await connectionStore.SetAsync(connectionRecord, cancellationToken).ConfigureAwait(false);
            dynamicConnectionRegistry.Invalidate(connectionRecord.Name);

            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.Added);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    #endregion Add

    #region Update

    public async ValueTask<ConnectionChangeResult> UpdateAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(connectionRecord);

        if (IsProtected(connectionRecord.Name))
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.NotAllowed);
        }

        ConnectionChangeResult? rejection = await PrepareAsync(connectionRecord, cancellationToken).ConfigureAwait(false);

        if (rejection is not null)
        {
            return rejection;
        }

        #endregion Validations

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await connectionStore.GetAsync(connectionRecord.Name, cancellationToken).ConfigureAwait(false) is null)
            {
                return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.NotFound);
            }

            await connectionStore.SetAsync(connectionRecord, cancellationToken).ConfigureAwait(false);
            // The next call rebuilds the service from the new record, the old one is retired
            dynamicConnectionRegistry.Invalidate(connectionRecord.Name);

            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.Updated);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    #endregion Update

    #region Remove

    public async ValueTask<ConnectionChangeResult> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        #region Validations

        ArgumentNullException.ThrowIfNull(name);

        if (IsProtected(name))
        {
            return new ConnectionChangeResult(name, ConnectionChangeStatus.NotAllowed);
        }

        #endregion Validations

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            bool removed = await connectionStore.RemoveAsync(name, cancellationToken).ConfigureAwait(false);

            // Always drop the local entry, even if the store did not know it (another node may have removed it)
            dynamicConnectionRegistry.Invalidate(name);

            return new ConnectionChangeResult(name, removed ? ConnectionChangeStatus.Removed : ConnectionChangeStatus.NotFound);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    #endregion Remove

    #region Dispose

    public void Dispose()
    {
        _mutationLock.Dispose();
    }

    #endregion Dispose

    #region Private

    // Static connections are read-only here, and the empty name is the static default
    private bool IsProtected(string name) => name.Length == 0 || connectionHandlerRegistry.Contains(name);

    // Checks the record, then opens the database. Returns the rejection, or null if the record is good
    private async Task<ConnectionChangeResult?> PrepareAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken)
    {
        IConnectionProvider? connectionProvider = dynamicConnectionRegistry.FindProvider(connectionRecord.Provider);

        if (connectionProvider is null)
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.UnknownProvider, new InvalidOperationException($"DB - No provider '{connectionRecord.Provider}' is registered"));
        }

        if (!connectionProvider.OptionsType.IsInstanceOfType(connectionRecord.Options))
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.Invalid, new ArgumentException($"DB - The {connectionRecord.Provider} provider expects options of type {connectionProvider.OptionsType.Name}"));
        }

        try
        {
            connectionRecord.Options.Validate();
        }
        catch (ArgumentException ex)
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.Invalid, ex);
        }

        try
        {
            await connectionProvider.TestAsync(connectionRecord, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionChangeResult(connectionRecord.Name, ConnectionChangeStatus.TestFailed, new InvalidOperationException($"DB - Could not connect to the {connectionRecord.Provider} connection '{connectionRecord.Name}'", ex));
        }

        return null;
    }

    #endregion Private
}