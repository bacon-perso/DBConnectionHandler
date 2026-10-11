using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Registries;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Tests.Core;

/// <summary>
/// How the registry disposes the services of a provider, whichever way those services can be disposed,
/// and how the container disposes the registry
/// </summary>
[TestFixture]
public class DynamicConnectionRegistryDisposalTests
{
    private TestHost _host = null!;

    private DynamicConnectionRegistry Registry => _host.ServiceProvider.GetRequiredService<DynamicConnectionRegistry>();

    [SetUp]
    public void SetUp()
    {
        _host = TestHost.Create(builder => builder.ConfigureDynamicConnections(o => o.RetireDelay = TimeSpan.Zero));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    private async Task<T> LoadAsync<T>(string name, Func<string, FakeConnectionHandlerServiceBase> serviceFactory) where T : FakeConnectionHandlerServiceBase
    {
        _host.Provider.ServiceFactory = serviceFactory;
        await _host.Manager.AddAsync(TestHost.Record(name));

        return (T)(await _host.Factory.GetHandlerAsync(name));
    }

    #region Retiring a service

    [Test]
    public async Task RetireDisposesAnAsyncOnlyService()
    {
        FakeConnectionHandlerService service = await LoadAsync<FakeConnectionHandlerService>("tenant-a", cs => new FakeConnectionHandlerService(cs));

        await _host.Manager.RemoveAsync("tenant-a");

        Assert.That(await TestHost.WaitForAsync(() => service.DisposeCount == 1), Is.True, "an async-only service was not disposed");
    }

    [Test]
    public async Task RetireDisposesASyncOnlyService()
    {
        SyncFakeConnectionHandlerService service = await LoadAsync<SyncFakeConnectionHandlerService>("tenant-a", cs => new SyncFakeConnectionHandlerService(cs));

        await _host.Manager.RemoveAsync("tenant-a");

        Assert.That(await TestHost.WaitForAsync(() => service.DisposeCount == 1), Is.True, "a sync-only service was not disposed");
    }

    [Test]
    public async Task RetirePrefersTheAsyncDisposalWhenBothAreSupported()
    {
        BothFakeConnectionHandlerService service = await LoadAsync<BothFakeConnectionHandlerService>("tenant-a", cs => new BothFakeConnectionHandlerService(cs));

        await _host.Manager.RemoveAsync("tenant-a");

        Assert.That(await TestHost.WaitForAsync(() => service.AsyncDisposeCount == 1), Is.True, "the async disposal was not used");
        Assert.That(service.SyncDisposeCount, Is.Zero, "a service that supports both must only be disposed once");
    }

    [Test]
    public async Task RetireLeavesAServiceThatOwnsNothingAlone()
    {
        await LoadAsync<PlainFakeConnectionHandlerService>("tenant-a", cs => new PlainFakeConnectionHandlerService(cs));

        ConnectionChangeResult removed = await _host.Manager.RemoveAsync("tenant-a");
        await Task.Delay(100);

        Assert.That(removed.Status, Is.EqualTo(ConnectionChangeStatus.Removed));
    }

    [Test]
    public async Task RetireKeepsGoingWhenADisposalThrows()
    {
        _host.Provider.ServiceFactory = cs => new ThrowingFakeConnectionHandlerService(cs);
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));
        await _host.Factory.GetHandlerAsync("tenant-a");

        await Assert.DoesNotThrowAsync(async () => await _host.Manager.RemoveAsync("tenant-a"));
        await Task.Delay(100);

        // The failure is logged, the registry is still usable
        Assert.That((await _host.Manager.AddAsync(TestHost.Record("tenant-a"))).Status, Is.EqualTo(ConnectionChangeStatus.Added));
    }

    private sealed class ThrowingFakeConnectionHandlerService(string connectionString) : FakeConnectionHandlerServiceBase(connectionString), IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("cannot dispose");
    }

    #endregion Retiring a service

    #region Disposing the registry

    private async Task<(FakeConnectionHandlerService Async, SyncFakeConnectionHandlerService Sync, BothFakeConnectionHandlerService Both)> LoadEveryKindAsync()
    {
        Dictionary<string, Func<string, FakeConnectionHandlerServiceBase>> factories = new()
        {
            { "async", cs => new FakeConnectionHandlerService(cs) },
            { "sync", cs => new SyncFakeConnectionHandlerService(cs) },
            { "both", cs => new BothFakeConnectionHandlerService(cs) }
        };

        foreach (KeyValuePair<string, Func<string, FakeConnectionHandlerServiceBase>> factory in factories)
        {
            _host.Provider.ServiceFactory = factory.Value;
            await _host.Manager.AddAsync(TestHost.Record(factory.Key));
            await _host.Factory.GetHandlerAsync(factory.Key);
        }

        return (
            (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("async"),
            (SyncFakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("sync"),
            (BothFakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("both"));
    }

    [Test]
    public async Task DisposeAsyncDisposesEveryKindOfService()
    {
        (FakeConnectionHandlerService asyncService, SyncFakeConnectionHandlerService syncService, BothFakeConnectionHandlerService bothService) = await LoadEveryKindAsync();

        await Registry.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(asyncService.DisposeCount, Is.EqualTo(1));
            Assert.That(syncService.DisposeCount, Is.EqualTo(1));
            Assert.That(bothService.AsyncDisposeCount, Is.EqualTo(1));
            Assert.That(bothService.SyncDisposeCount, Is.Zero);
            Assert.That(Registry.MaterializedNames, Is.Empty);
        });
    }

    [Test]
    public async Task DisposeDisposesEveryKindOfService()
    {
        (FakeConnectionHandlerService asyncService, SyncFakeConnectionHandlerService syncService, BothFakeConnectionHandlerService bothService) = await LoadEveryKindAsync();

        Registry.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(asyncService.DisposeCount, Is.EqualTo(1));
            Assert.That(syncService.DisposeCount, Is.EqualTo(1));
            Assert.That(bothService.AsyncDisposeCount, Is.EqualTo(1));
            Assert.That(bothService.SyncDisposeCount, Is.Zero);
            Assert.That(Registry.MaterializedNames, Is.Empty);
        });
    }

    [Test]
    public async Task DisposeWaitsForAnAsyncServiceThatReallyTakesTime()
    {
        FakeConnectionHandlerService service = await LoadAsync<FakeConnectionHandlerService>("tenant-a", cs => new FakeConnectionHandlerService(cs) { DisposeDelay = TimeSpan.FromMilliseconds(100) });

        Registry.Dispose();

        Assert.That(service.DisposeCount, Is.EqualTo(1), "Dispose() returned before the service was disposed");
    }

    [Test]
    public async Task DisposeKeepsDisposingTheOthersWhenOneThrows()
    {
        _host.Provider.ServiceFactory = cs => new ThrowingFakeConnectionHandlerService(cs);
        await _host.Manager.AddAsync(TestHost.Record("bad"));
        await _host.Factory.GetHandlerAsync("bad");
        SyncFakeConnectionHandlerService good = await LoadAsync<SyncFakeConnectionHandlerService>("good", cs => new SyncFakeConnectionHandlerService(cs));

        Assert.DoesNotThrow(Registry.Dispose);

        Assert.That(good.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeDoesNotDeadlockOnAThreadWhoseSynchronizationContextIsNeverPumped()
    {
        // Like a UI thread that is blocked waiting for Dispose(): any continuation posted back to it would never run
        await LoadAsync<FakeConnectionHandlerService>("tenant-a", cs => new FakeConnectionHandlerService(cs) { DisposeDelay = TimeSpan.FromMilliseconds(50) });
        DynamicConnectionRegistry registry = Registry;

        Task disposing = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());
            registry.Dispose();
        });

        Task finished = await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(finished, Is.SameAs(disposing), "Dispose() deadlocked");
    }

    private sealed class NeverPumpedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Dropped on purpose: nothing ever runs the callbacks
        }
    }

    #endregion Disposing the registry

    #region Disposing the container

    [Test]
    public async Task ServiceProviderCanBeDisposedSynchronouslyWhenTheRegistryWasCreated()
    {
        TestHost host = TestHost.Create();
        await host.Manager.AddAsync(TestHost.Record("tenant-a"));
        await host.Factory.GetHandlerAsync("tenant-a");

        // This used to throw: the registry only supported DisposeAsync
        Assert.DoesNotThrow(host.ServiceProvider.Dispose);
    }

    [Test]
    public async Task ServiceProviderDisposesTheLoadedServicesWhenDisposedSynchronously()
    {
        TestHost host = TestHost.Create(provider: new FakeConnectionProvider { ServiceFactory = cs => new SyncFakeConnectionHandlerService(cs) });
        await host.Manager.AddAsync(TestHost.Record("tenant-a"));
        SyncFakeConnectionHandlerService service = (SyncFakeConnectionHandlerService)await host.Factory.GetHandlerAsync("tenant-a");

        host.ServiceProvider.Dispose();

        Assert.That(service.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ServiceProviderDisposesTheLoadedServicesWhenDisposedAsynchronously()
    {
        TestHost host = TestHost.Create(provider: new FakeConnectionProvider { ServiceFactory = cs => new SyncFakeConnectionHandlerService(cs) });
        await host.Manager.AddAsync(TestHost.Record("tenant-a"));
        SyncFakeConnectionHandlerService service = (SyncFakeConnectionHandlerService)await host.Factory.GetHandlerAsync("tenant-a");

        await host.ServiceProvider.DisposeAsync();

        Assert.That(service.DisposeCount, Is.EqualTo(1));
    }

    #endregion Disposing the container
}
