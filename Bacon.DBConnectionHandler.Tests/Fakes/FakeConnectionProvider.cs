using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Services;
using System.Collections.Concurrent;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A provider that never touches a database. It counts what it is asked to do and can be told to fail its test
/// </summary>
internal sealed class FakeConnectionProvider(string providerId) : BaseConnectionProvider<FakeConnectionHandlerOptions>
{
    public const string DefaultProviderId = "fake";

    private readonly ConcurrentQueue<IConnectionHandlerService> _created = new();
    private int _createCount;
    private int _testCount;

    // DI needs a constructor it can call
    public FakeConnectionProvider() : this(DefaultProviderId)
    {
    }

    public override string Provider => providerId;

    public int CreateCount => Volatile.Read(ref _createCount);

    public int TestCount => Volatile.Read(ref _testCount);

    /// <summary>
    /// Every service created so far, in creation order
    /// </summary>
    public IReadOnlyList<IConnectionHandlerService> Created => [.. _created];

    /// <summary>
    /// Builds the service for a connection string, to get a service that is disposed another way. Async-only by default
    /// </summary>
    public Func<string, FakeConnectionHandlerServiceBase>? ServiceFactory { get; set; }

    /// <summary>
    /// Returns the exception the next test must throw for these options, or null to let it pass
    /// </summary>
    public Func<FakeConnectionHandlerOptions, Exception?>? TestFailure { get; set; }

    protected override IConnectionHandlerService Create(string name, FakeConnectionHandlerOptions options, IServiceProvider serviceProvider)
    {
        Interlocked.Increment(ref _createCount);

        FakeConnectionHandlerServiceBase service = ServiceFactory?.Invoke(options.ConnectionString) ?? new FakeConnectionHandlerService(options.ConnectionString);
        _created.Enqueue(service);

        return service;
    }

    protected override Task TestAsync(FakeConnectionHandlerOptions options, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _testCount);

        cancellationToken.ThrowIfCancellationRequested();

        Exception? failure = TestFailure?.Invoke(options);

        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}
