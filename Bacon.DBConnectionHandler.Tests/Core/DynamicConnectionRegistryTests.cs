using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Registries;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class DynamicConnectionRegistryTests
{
    private RecordingConnectionStore _store = null!;
    private ManualTimeProvider _timeProvider = null!;
    private TestHost _host = null!;
    private DynamicConnectionRegistry Registry => _host.ServiceProvider.GetRequiredService<DynamicConnectionRegistry>();

    [SetUp]
    public void SetUp()
    {
        _store = new RecordingConnectionStore();
        _timeProvider = new ManualTimeProvider();
        _host = CreateHost(TimeSpan.Zero, cacheDuration: null);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    private TestHost CreateHost(TimeSpan retireDelay, TimeSpan? cacheDuration)
    {
        return TestHost.Create(
            builder => builder.ConfigureDynamicConnections(o =>
            {
                o.RetireDelay = retireDelay;
                o.CacheDuration = cacheDuration;
            }),
            _store,
            _timeProvider);
    }

    private async Task SeedAsync(string name, string connectionString = "tenant-string")
    {
        await _store.SetAsync(TestHost.Record(name, connectionString));
    }

    #region GetAsync

    [Test]
    public async Task GetAsyncReturnsNullWhenTheStoreHasNoRecord()
    {
        Assert.That(await Registry.GetAsync("missing", CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task GetAsyncDoesNotCacheAnUnknownName()
    {
        Assert.That(await Registry.GetAsync("tenant-a", CancellationToken.None), Is.Null);

        await SeedAsync("tenant-a");

        Assert.That(await Registry.GetAsync("tenant-a", CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task GetAsyncBuildsTheServiceFromTheRecord()
    {
        await SeedAsync("tenant-a", "the-string");

        IConnectionHandlerService? service = await Registry.GetAsync("tenant-a", CancellationToken.None);

        Assert.That(((FakeConnectionHandlerService)service!).ConnectionString, Is.EqualTo("the-string"));
    }

    [Test]
    public async Task GetAsyncReturnsTheSameInstanceAndReadsTheStoreOnce()
    {
        await SeedAsync("tenant-a");

        IConnectionHandlerService? first = await Registry.GetAsync("tenant-a", CancellationToken.None);
        IConnectionHandlerService? second = await Registry.GetAsync("tenant-a", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(_store.GetCount, Is.EqualTo(1));
            Assert.That(_host.Provider.CreateCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetAsyncLoadsOnlyOnceWhenManyCallersAskAtTheSameTime()
    {
        await SeedAsync("tenant-a");

        IConnectionHandlerService?[] results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(async () => await Registry.GetAsync("tenant-a", CancellationToken.None))));

        Assert.Multiple(() =>
        {
            Assert.That(results.Distinct().Count(), Is.EqualTo(1));
            Assert.That(_host.Provider.CreateCount, Is.EqualTo(1));
            Assert.That(_store.GetCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetAsyncThrowsWhenNoProviderIsRegisteredForTheRecord()
    {
        await _store.SetAsync(TestHost.Record("tenant-a", providerId: "unregistered"));

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Registry.GetAsync("tenant-a", CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("unregistered"));
    }

    [Test]
    public async Task GetAsyncDoesNotCacheAFailedLoad()
    {
        await _store.SetAsync(TestHost.Record("tenant-a", providerId: "unregistered"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Registry.GetAsync("tenant-a", CancellationToken.None));

        await SeedAsync("tenant-a");

        Assert.That(await Registry.GetAsync("tenant-a", CancellationToken.None), Is.Not.Null);
    }

    [Test]
    public async Task GetAsyncThrowsOperationCanceledExceptionWhenTheTokenIsCancelledWhileLoading()
    {
        await SeedAsync("tenant-a");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.GetGate = gate.Task;
        using CancellationTokenSource cancellationTokenSource = new();
        Task<IConnectionHandlerService?> loading = Registry.GetAsync("tenant-a", cancellationTokenSource.Token).AsTask();

        await cancellationTokenSource.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(async () => await loading);
        gate.SetResult();
    }

    [Test]
    public async Task GetAsyncStillLoadsAfterACallerCancelled()
    {
        await SeedAsync("tenant-a");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.GetGate = gate.Task;
        using CancellationTokenSource cancellationTokenSource = new();
        Task<IConnectionHandlerService?> cancelled = Registry.GetAsync("tenant-a", cancellationTokenSource.Token).AsTask();
        await cancellationTokenSource.CancelAsync();
        await Assert.CatchAsync<OperationCanceledException>(async () => await cancelled);

        // The load the cancelled caller started is shared, it must neither fail nor be repeated for the next caller
        Task<IConnectionHandlerService?> next = Registry.GetAsync("tenant-a", CancellationToken.None).AsTask();
        gate.SetResult();

        Assert.Multiple(async () =>
        {
            Assert.That(await next, Is.Not.Null);
            Assert.That(_host.Provider.CreateCount, Is.EqualTo(1));
        });
    }

    #endregion GetAsync

    #region Names And Providers

    [Test]
    public async Task MaterializedNamesListsOnlyTheLoadedConnections()
    {
        await SeedAsync("tenant-a");
        await SeedAsync("tenant-b");
        await Registry.GetAsync("tenant-a", CancellationToken.None);

        Assert.That(Registry.MaterializedNames, Is.EquivalentTo(["tenant-a"]));
    }

    [Test]
    public void FindProviderIsNotCaseSensitive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Registry.FindProvider("FAKE"), Is.SameAs(_host.Provider));
            Assert.That(Registry.FindProvider("unknown"), Is.Null);
        });
    }

    #endregion Names And Providers

    #region Invalidate

    [Test]
    public async Task InvalidateForcesAReloadAndRetiresTheOldService()
    {
        await SeedAsync("tenant-a", "old");
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;
        await _store.SetAsync(TestHost.Record("tenant-a", "new"));

        Registry.Invalidate("tenant-a");

        FakeConnectionHandlerService reloaded = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;
        Assert.Multiple(() =>
        {
            Assert.That(reloaded, Is.Not.SameAs(old));
            Assert.That(reloaded.ConnectionString, Is.EqualTo("new"));
        });
        Assert.That(await TestHost.WaitForAsync(() => old.DisposeCount == 1), Is.True, "the old service was not disposed");
    }

    [Test]
    public async Task InvalidateDoesNotDisposeTheOldServiceBeforeTheRetireDelay()
    {
        await _host.DisposeAsync();
        _host = CreateHost(TimeSpan.FromHours(1), cacheDuration: null);
        await SeedAsync("tenant-a");
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;

        Registry.Invalidate("tenant-a");
        await Task.Delay(200);

        Assert.That(old.DisposeCount, Is.Zero);
    }

    [Test]
    public void InvalidateDoesNothingForAnUnknownName()
    {
        Assert.DoesNotThrow(() => Registry.Invalidate("missing"));
    }

    [Test]
    public async Task InvalidateRetiresAServiceThatWasStillLoading()
    {
        await SeedAsync("tenant-a");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.GetGate = gate.Task;
        Task<IConnectionHandlerService?> loading = Registry.GetAsync("tenant-a", CancellationToken.None).AsTask();

        // The load is blocked in the store: dropping the entry now must not leak what it eventually builds
        Registry.Invalidate("tenant-a");
        gate.SetResult();
        await loading;

        FakeConnectionHandlerService service = (FakeConnectionHandlerService)_host.Provider.Created.Single();
        Assert.That(await TestHost.WaitForAsync(() => service.DisposeCount == 1), Is.True, "a service dropped while loading was leaked");
    }

    #endregion Invalidate

    #region Expiry

    [Test]
    public async Task GetAsyncReturnsTheSameInstanceBeforeTheCacheDurationEnds()
    {
        await _host.DisposeAsync();
        _host = CreateHost(TimeSpan.Zero, TimeSpan.FromMinutes(5));
        await SeedAsync("tenant-a");
        IConnectionHandlerService? first = await Registry.GetAsync("tenant-a", CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(4));

        Assert.That(await Registry.GetAsync("tenant-a", CancellationToken.None), Is.SameAs(first));
    }

    [Test]
    public async Task GetAsyncReloadsFromTheStoreAfterTheCacheDurationEnds()
    {
        await _host.DisposeAsync();
        _host = CreateHost(TimeSpan.Zero, TimeSpan.FromMinutes(5));
        await SeedAsync("tenant-a", "old");
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;
        await _store.SetAsync(TestHost.Record("tenant-a", "changed-by-another-node"));

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        FakeConnectionHandlerService reloaded = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.ConnectionString, Is.EqualTo("changed-by-another-node"));
            Assert.That(reloaded, Is.Not.SameAs(old));
        });
        Assert.That(await TestHost.WaitForAsync(() => old.DisposeCount == 1), Is.True, "the expired service was not retired");
    }

    [Test]
    public async Task GetAsyncReturnsNullWhenTheRecordWasRemovedAndTheEntryExpired()
    {
        await _host.DisposeAsync();
        _host = CreateHost(TimeSpan.Zero, TimeSpan.FromMinutes(5));
        await SeedAsync("tenant-a");
        await Registry.GetAsync("tenant-a", CancellationToken.None);
        await _store.RemoveAsync("tenant-a");

        _timeProvider.Advance(TimeSpan.FromMinutes(6));

        Assert.That(await Registry.GetAsync("tenant-a", CancellationToken.None), Is.Null);
    }

    #endregion Expiry

    #region Dispose

    [Test]
    public async Task DisposeAsyncDisposesTheLiveServices()
    {
        await SeedAsync("tenant-a");
        await SeedAsync("tenant-b");
        FakeConnectionHandlerService a = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-a", CancellationToken.None))!;
        FakeConnectionHandlerService b = (FakeConnectionHandlerService)(await Registry.GetAsync("tenant-b", CancellationToken.None))!;

        await Registry.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(a.DisposeCount, Is.EqualTo(1));
            Assert.That(b.DisposeCount, Is.EqualTo(1));
            Assert.That(Registry.MaterializedNames, Is.Empty);
        });
    }

    #endregion Dispose
}
