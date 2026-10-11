using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionHandlerFactoryTests
{
    private TestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = TestHost.Create(builder => builder
            .Add(TestHost.StaticHandle(string.Empty))
            .Add(TestHost.StaticHandle("reporting")));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    #region Static

    [Test]
    public async Task GetHandlerAsyncReturnsTheDefaultConnectionWhenNoNameIsGiven()
    {
        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync();

        Assert.That(handler, Is.SameAs(_host.ServiceProvider.GetRequiredService<IConnectionHandlerService>()));
    }

    [Test]
    public async Task GetHandlerAsyncReturnsANamedConnection()
    {
        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync("reporting");

        Assert.That(((FakeConnectionHandlerService)handler).ConnectionString, Is.EqualTo("static-reporting"));
    }

    [Test]
    public async Task GetHandlerAsyncReturnsTheKeyedStaticInstance()
    {
        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync("reporting");

        Assert.That(handler, Is.SameAs(_host.ServiceProvider.GetRequiredKeyedService<IConnectionHandlerService>("reporting")));
    }

    [Test]
    public async Task GetHandlerAsyncThrowsArgumentNullExceptionWhenTheNameIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _host.Factory.GetHandlerAsync(null!));
    }

    [Test]
    public async Task GetHandlerAsyncThrowsInvalidOperationExceptionWhenTheNameIsUnknown()
    {
        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _host.Factory.GetHandlerAsync("missing"));

        Assert.That(exception!.Message, Does.Contain("Unknown connection 'missing'"));
    }

    [Test]
    public async Task GetHandlerAsyncDoesNotLookTheEmptyNameUpInTheStore()
    {
        RecordingConnectionStore store = new();
        await using TestHost host = TestHost.Create(builder => builder.Add(TestHost.StaticHandle("named")), store);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Factory.GetHandlerAsync());
        Assert.That(store.GetCount, Is.Zero);
    }

    [Test]
    public async Task GetHandlerAsyncSaysThatBuildWasNotCalledWhenTheRegistryIsNotBuilt()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddConnectionHandler();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await serviceProvider.GetRequiredService<IConnectionHandlerFactory>().GetHandlerAsync("a"));

        Assert.That(exception!.Message, Does.Contain("Build()"));
    }

    #endregion Static

    #region Dynamic

    [Test]
    public async Task GetHandlerAsyncLoadsADynamicConnectionFromTheStore()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "tenant-a-string"));

        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync("tenant-a");

        Assert.That(((FakeConnectionHandlerService)handler).ConnectionString, Is.EqualTo("tenant-a-string"));
    }

    [Test]
    public async Task GetHandlerAsyncReturnsTheSameInstanceUntilTheConnectionChanges()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        IConnectionHandlerService first = await _host.Factory.GetHandlerAsync("tenant-a");
        IConnectionHandlerService second = await _host.Factory.GetHandlerAsync("tenant-a");

        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public async Task NamesListsTheStaticConnectionsAndOnlyTheDynamicOnesThatAreLoaded()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));
        await _host.Manager.AddAsync(TestHost.Record("tenant-b"));
        Assert.That(_host.Factory.Names, Is.EquivalentTo([string.Empty, "reporting"]));

        await _host.Factory.GetHandlerAsync("tenant-a");

        Assert.That(_host.Factory.Names, Is.EquivalentTo([string.Empty, "reporting", "tenant-a"]));
    }

    [Test]
    public async Task GetHandlerAsyncCanBeCancelled()
    {
        RecordingConnectionStore store = new();
        await using TestHost host = TestHost.Create(store: store);
        await host.Manager.AddAsync(TestHost.Record("tenant-a"));
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.GetGate = gate.Task;
        using CancellationTokenSource cancellationTokenSource = new();
        Task<IConnectionHandlerService> loading = host.Factory.GetHandlerAsync("tenant-a", cancellationTokenSource.Token).AsTask();

        await cancellationTokenSource.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(async () => await loading);
        gate.SetResult();
    }

    #endregion Dynamic
}
