using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;

namespace Bacon.DBConnectionHandler.Services;

/// <summary>
/// Typed base for <see cref="IConnectionProvider"/>: does the options cast once so a provider only deals with its own options
/// </summary>
/// <typeparam name="TOptions">The provider's options type</typeparam>
public abstract class BaseConnectionProvider<TOptions> : IConnectionProvider where TOptions : ConnectionHandlerOptions
{
    /// <inheritdoc />
    public abstract string Provider { get; }

    /// <inheritdoc />
    public Type OptionsType => typeof(TOptions);

    /// <inheritdoc />
    public IConnectionHandlerService Create(ConnectionRecord connectionRecord, IServiceProvider serviceProvider)
    {
        return Create(connectionRecord.Name, GetOptions(connectionRecord), serviceProvider);
    }

    /// <inheritdoc />
    public Task TestAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken)
    {
        return TestAsync(GetOptions(connectionRecord), cancellationToken);
    }

    /// <summary>
    /// Creates the service. If it implements <see cref="IAsyncDisposable"/>, it is disposed when the connection is retired
    /// </summary>
    protected abstract IConnectionHandlerService Create(string name, TOptions options, IServiceProvider serviceProvider);

    /// <summary>
    /// Opens a connection to validate the record. Throws if the database is unreachable
    /// </summary>
    protected abstract Task TestAsync(TOptions options, CancellationToken cancellationToken);

    private static TOptions GetOptions(ConnectionRecord connectionRecord)
    {
        return connectionRecord.Options as TOptions ?? throw new ArgumentException($"DB - The options must be of type {typeof(TOptions).Name}", nameof(connectionRecord));
    }
}