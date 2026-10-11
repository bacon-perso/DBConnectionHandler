using Bacon.DBConnectionHandler.Models;

namespace Bacon.DBConnectionHandler.Interfaces.Services;

/// <summary>
/// Turns a <see cref="ConnectionRecord"/> into a live service. One per database provider.
/// Implement through <see cref="DBConnectionHandler.Services.BaseConnectionProvider{TOptions}"/>
/// </summary>
public interface IConnectionProvider
{
    /// <summary>
    /// The provider id (e.g. "mssql"), matched against <see cref="ConnectionRecord.Provider"/>
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// The options type this provider expects. A store that serializes records uses it to deserialize <see cref="ConnectionRecord.Options"/>
    /// </summary>
    Type OptionsType { get; }

    /// <summary>
    /// Creates the service. If it implements <see cref="IAsyncDisposable"/>, it is disposed when the connection is retired
    /// </summary>
    IConnectionHandlerService Create(ConnectionRecord connectionRecord, IServiceProvider serviceProvider);

    /// <summary>
    /// Opens a connection to validate the record. Throws if the database is unreachable
    /// </summary>
    Task TestAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken);
}