using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Registries;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Stores;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionHandlerBuilderTests
{
    private ServiceCollection _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection();
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    #region AddConnectionHandler

    [Test]
    public void AddConnectionHandlerThrowsArgumentNullExceptionWhenServicesIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ConnectionHandlerExtensions.AddConnectionHandler(null!));
    }

    [Test]
    public async Task AddConnectionHandlerRegistersTheDynamicSideWithItsDefaults()
    {
        _services.AddConnectionHandler().AddProvider<FakeConnectionProvider>().Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();

        Assert.Multiple(() =>
        {
            Assert.That(serviceProvider.GetRequiredService<IConnectionStore>(), Is.TypeOf<InMemoryConnectionHandlerStore>());
            Assert.That(serviceProvider.GetRequiredService<IConnectionHandlerManager>(), Is.SameAs(manager));
            Assert.That(serviceProvider.GetRequiredService<DynamicConnectionOptions>().CacheDuration, Is.Null);
            Assert.That(serviceProvider.GetRequiredService<TimeProvider>(), Is.SameAs(TimeProvider.System));
        });
    }

    [Test]
    public async Task AddConnectionHandlerRegistersTheFactoryAsTransient()
    {
        _services.AddConnectionHandler().AddProvider<FakeConnectionProvider>().Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        IConnectionHandlerFactory first = serviceProvider.GetRequiredService<IConnectionHandlerFactory>();
        IConnectionHandlerFactory second = serviceProvider.GetRequiredService<IConnectionHandlerFactory>();

        Assert.That(first, Is.Not.SameAs(second));
    }

    #endregion AddConnectionHandler

    #region Add

    [Test]
    public void AddThrowsArgumentNullExceptionWhenArgumentsAreNull()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ConnectionHandlerBuilderExtensions.Add(null!, TestHost.StaticHandle("a")));
            Assert.Throws<ArgumentNullException>(() => builder.Add(null!));
        });
    }

    [Test]
    public void AddThrowsInvalidOperationExceptionWhenTheNameIsAlreadyUsed()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler().Add(TestHost.StaticHandle("reporting"));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Add(TestHost.StaticHandle("reporting")));

        Assert.That(exception!.Message, Does.Contain("reporting"));
    }

    [Test]
    public void AddThrowsInvalidOperationExceptionWhenASecondDefaultIsAdded()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler().Add(TestHost.StaticHandle(string.Empty));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Add(TestHost.StaticHandle(string.Empty)));

        Assert.That(exception!.Message, Does.Contain("default"));
    }

    [Test]
    public void AddReturnsTheBuilderForChaining()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler();

        Assert.That(builder.Add(TestHost.StaticHandle("a")), Is.SameAs(builder));
    }

    #endregion Add

    #region AddProvider

    [Test]
    public void AddProviderRegistersTheProviderOnceEvenWhenCalledTwice()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler();

        builder.AddProvider<FakeConnectionProvider>().AddProvider<FakeConnectionProvider>();

        Assert.That(_services.Count(descriptor => descriptor.ServiceType == typeof(IConnectionProvider)), Is.EqualTo(1));
    }

    [Test]
    public void AddProviderThrowsArgumentNullExceptionWhenTheBuilderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ConnectionHandlerBuilderExtensions.AddProvider<FakeConnectionProvider>(null!));
    }

    #endregion AddProvider

    #region Configuration

    [Test]
    public async Task UseStoreReplacesTheDefaultStore()
    {
        _services.AddConnectionHandler().AddProvider<FakeConnectionProvider>().UseStore<RecordingConnectionStore>().Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(serviceProvider.GetRequiredService<IConnectionStore>(), Is.TypeOf<RecordingConnectionStore>());
            Assert.That(serviceProvider.GetServices<IConnectionStore>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConfigureDynamicConnectionsChangesTheOptionsTheRegistryUses()
    {
        _services.AddConnectionHandler()
            .AddProvider<FakeConnectionProvider>()
            .ConfigureDynamicConnections(o =>
            {
                o.CacheDuration = TimeSpan.FromMinutes(3);
                o.RetireDelay = TimeSpan.Zero;
            })
            .Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        DynamicConnectionOptions options = serviceProvider.GetRequiredService<DynamicConnectionOptions>();

        Assert.Multiple(() =>
        {
            Assert.That(options.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(options.RetireDelay, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ConfigureDynamicConnectionsThrowsArgumentNullExceptionWhenTheActionIsNull()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler();

        Assert.Throws<ArgumentNullException>(() => builder.ConfigureDynamicConnections(null!));
    }

    #endregion Configuration

    #region Build

    [Test]
    public void BuildThrowsInvalidOperationExceptionWhenNothingWasAdded()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public void BuildSucceedsWithOnlyAProvider()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler().AddProvider<FakeConnectionProvider>();

        Assert.DoesNotThrow(() => builder.Build());
    }

    [Test]
    public void BuildReturnsTheServiceCollection()
    {
        IServiceCollection result = _services.AddConnectionHandler().Add(TestHost.StaticHandle("a")).Build();

        Assert.That(result, Is.SameAs(_services));
    }

    [Test]
    public void BuildMarksTheStaticRegistryAsBuilt()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler().AddProvider<FakeConnectionProvider>();
        Assert.That(builder.Registry.IsBuilt, Is.False);

        builder.Build();

        Assert.That(builder.Registry.IsBuilt, Is.True);
    }

    [Test]
    public void BuildOpensEveryStaticConnectionOnce()
    {
        int opened = 0;
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler()
            .Add(TestHost.StaticHandle("a", () => Interlocked.Increment(ref opened)))
            .Add(TestHost.StaticHandle("b", () => Interlocked.Increment(ref opened)));

        builder.Build();

        Assert.That(opened, Is.EqualTo(2));
    }

    [Test]
    public void BuildThrowsAggregateExceptionListingEveryConnectionThatFailed()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler()
            .Add(TestHost.StaticHandle(string.Empty, () => throw new InvalidOperationException("down")))
            .Add(TestHost.StaticHandle("a", () => throw new InvalidOperationException("down")))
            .Add(TestHost.StaticHandle("fine"));

        AggregateException? exception = Assert.Throws<AggregateException>(() => builder.Build());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("DB - Could not connect to: default, a"));
            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void BuildRegistersNothingWhenAConnectionCannotBeOpened()
    {
        ConnectionHandlerBuilder builder = _services.AddConnectionHandler()
            .Add(TestHost.StaticHandle("fine"))
            .Add(TestHost.StaticHandle("down", () => throw new InvalidOperationException("down")));

        Assert.Throws<AggregateException>(() => builder.Build());

        Assert.Multiple(() =>
        {
            Assert.That(_services.Any(descriptor => descriptor.IsKeyedService), Is.False);
            Assert.That(builder.Registry.IsBuilt, Is.False);
        });
    }

    [Test]
    public async Task BuildRegistersEachConnectionAsAKeyedSingleton()
    {
        int created = 0;
        ConnectionHandle handle = new(
            TestHost.Record("a"),
            _ =>
            {
                Interlocked.Increment(ref created);

                return new FakeConnectionHandlerService("a");
            },
            () => { });
        _services.AddConnectionHandler().Add(handle).Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        IConnectionHandlerService first = serviceProvider.GetRequiredKeyedService<IConnectionHandlerService>("a");
        IConnectionHandlerService second = serviceProvider.GetRequiredKeyedService<IConnectionHandlerService>("a");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(created, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BuildExposesTheDefaultConnectionAsAPlainService()
    {
        _services.AddConnectionHandler().Add(TestHost.StaticHandle(string.Empty)).Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        Assert.That(serviceProvider.GetRequiredService<IConnectionHandlerService>(), Is.SameAs(serviceProvider.GetRequiredKeyedService<IConnectionHandlerService>(string.Empty)));
    }

    [Test]
    public async Task BuildDoesNotExposeAPlainServiceWhenThereIsNoDefaultConnection()
    {
        _services.AddConnectionHandler().Add(TestHost.StaticHandle("named")).Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        Assert.That(serviceProvider.GetService<IConnectionHandlerService>(), Is.Null);
    }

    [Test]
    public async Task BuildRunsTheConfigureServicesOfEachConnection()
    {
        bool configured = false;
        ConnectionHandle handle = new(TestHost.Record("a"), _ => new FakeConnectionHandlerService("a"), () => { }, services =>
        {
            configured = true;
            services.AddSingleton("marker");
        });

        _services.AddConnectionHandler().Add(handle).Build();
        await using ServiceProvider serviceProvider = _services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(configured, Is.True);
            Assert.That(serviceProvider.GetRequiredService<string>(), Is.EqualTo("marker"));
        });
    }

    [Test]
    public void BuildThrowsArgumentNullExceptionWhenTheBuilderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ConnectionHandlerBuilderExtensions.Build(null!));
    }

    #endregion Build
}
