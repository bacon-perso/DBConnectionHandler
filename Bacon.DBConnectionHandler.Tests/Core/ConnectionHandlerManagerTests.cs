using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Tests.Fakes;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionHandlerManagerTests
{
    private RecordingConnectionStore _store = null!;
    private TestHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new RecordingConnectionStore();
        _host = CreateHost(_store);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    // A static default and a static named connection exist, and a replaced service is retired right away
    private static TestHost CreateHost(Interfaces.Stores.IConnectionStore store)
    {
        return TestHost.Create(
            builder => builder
                .Add(TestHost.StaticHandle(string.Empty))
                .Add(TestHost.StaticHandle("main"))
                .ConfigureDynamicConnections(o => o.RetireDelay = TimeSpan.Zero),
            store);
    }

    #region Add

    [Test]
    public async Task AddAsyncReturnsAddedAndTheConnectionCanBeResolved()
    {
        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a", "tenant-a-string"));

        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync("tenant-a");
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Added));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Name, Is.EqualTo("tenant-a"));
            Assert.That(((FakeConnectionHandlerService)handler).ConnectionString, Is.EqualTo("tenant-a-string"));
        });
    }

    [Test]
    public async Task AddAsyncOpensTheDatabaseOnceBeforeStoringIt()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        Assert.That(_host.Provider.TestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AddAsyncReturnsAlreadyExistsForAStaticName()
    {
        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("main"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.AlreadyExists));
            Assert.That(_host.Provider.TestCount, Is.Zero, "a refused name must not open the database");
        });
    }

    [Test]
    public async Task AddAsyncReturnsAlreadyExistsForTheStaticDefaultName()
    {
        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record(string.Empty));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.AlreadyExists));
    }

    [Test]
    public async Task AddAsyncReturnsNotAllowedForTheEmptyNameWhenThereIsNoStaticDefault()
    {
        await using TestHost host = TestHost.Create(builder => builder.Add(TestHost.StaticHandle("main")));

        ConnectionChangeResult result = await host.Manager.AddAsync(TestHost.Record(string.Empty));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
    }

    [Test]
    public async Task AddAsyncReturnsAlreadyExistsForADynamicDuplicate()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "first"));

        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a", "second"));

        IConnectionHandlerService handler = await _host.Factory.GetHandlerAsync("tenant-a");
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.AlreadyExists));
            Assert.That(((FakeConnectionHandlerService)handler).ConnectionString, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task AddAsyncReturnsUnknownProviderWhenNoProviderMatches()
    {
        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a", providerId: "oracle"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.UnknownProvider));
            Assert.That(result.Error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(result.Error!.Message, Does.Contain("oracle"));
        });
    }

    [Test]
    public async Task AddAsyncReturnsInvalidWhenTheOptionsAreOfAnotherProvider()
    {
        ConnectionRecord record = new("tenant-a", FakeConnectionProvider.DefaultProviderId, new OtherFakeConnectionHandlerOptions { ConnectionString = "x" });

        ConnectionChangeResult result = await _host.Manager.AddAsync(record);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Invalid));
            Assert.That(result.Error, Is.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public async Task AddAsyncReturnsInvalidWhenTheOptionsFailTheirValidation()
    {
        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a", connectionString: string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Invalid));
            Assert.That(result.Error, Is.InstanceOf<ArgumentException>());
            Assert.That(_host.Provider.TestCount, Is.Zero, "invalid options must not open the database");
        });
    }

    [Test]
    public async Task AddAsyncReturnsTestFailedAndStoresNothingWhenTheDatabaseCannotBeOpened()
    {
        _host.Provider.TestFailure = _ => new InvalidOperationException("unreachable");

        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.TestFailed));
            Assert.That(result.Error!.InnerException!.Message, Is.EqualTo("unreachable"));
            Assert.That(result.Error.Message, Does.Contain("tenant-a"));
            Assert.That(await _store.GetAsync("tenant-a"), Is.Null);
            Assert.That(await _host.Manager.GetNamesAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task AddAsyncSucceedsAfterAFailedAttempt()
    {
        _host.Provider.TestFailure = _ => new InvalidOperationException("unreachable");
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));
        _host.Provider.TestFailure = null;

        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Added));
    }

    [Test]
    public async Task AddAsyncThrowsArgumentNullExceptionWhenTheRecordIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _host.Manager.AddAsync(null!));
    }

    [Test]
    public async Task AddAsyncRethrowsWhenTheCallerCancels()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await _host.Manager.AddAsync(TestHost.Record("tenant-a"), cancellationTokenSource.Token));
    }

    [Test]
    public async Task AddAsyncThrowsWhenTheStoreFails()
    {
        _store.SetFailure = new InvalidOperationException("store is down");

        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _host.Manager.AddAsync(TestHost.Record("tenant-a")));

        Assert.That(exception!.Message, Is.EqualTo("store is down"));
    }

    [Test]
    public async Task AddAsyncAddsExactlyOnceWhenCalledConcurrentlyForTheSameName()
    {
        ConnectionChangeResult[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () => (await _host.Manager.AddAsync(TestHost.Record("tenant-a"))))));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(r => r.Status == ConnectionChangeStatus.Added), Is.EqualTo(1));
            Assert.That(results.Count(r => r.Status == ConnectionChangeStatus.AlreadyExists), Is.EqualTo(9));
        });
    }

    #endregion Add

    #region Update

    [Test]
    public async Task UpdateAsyncReturnsUpdatedAndTheNextCallUsesTheNewDefinition()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "old"));
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a");

        ConnectionChangeResult result = await _host.Manager.UpdateAsync(TestHost.Record("tenant-a", "new"));

        FakeConnectionHandlerService current = (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a");
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Updated));
            Assert.That(current.ConnectionString, Is.EqualTo("new"));
            Assert.That(current, Is.Not.SameAs(old));
        });
    }

    [Test]
    public async Task UpdateAsyncRetiresThePreviousService()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "old"));
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a");

        await _host.Manager.UpdateAsync(TestHost.Record("tenant-a", "new"));

        Assert.That(await TestHost.WaitForAsync(() => old.DisposeCount == 1), Is.True, "the replaced service was not disposed");
    }

    [Test]
    public async Task UpdateAsyncReturnsNotFoundWhenTheConnectionDoesNotExist()
    {
        ConnectionChangeResult result = await _host.Manager.UpdateAsync(TestHost.Record("tenant-a"));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotFound));
    }

    [Test]
    public async Task UpdateAsyncReturnsNotAllowedForAStaticConnection()
    {
        ConnectionChangeResult result = await _host.Manager.UpdateAsync(TestHost.Record("main", "changed"));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
            Assert.That(((FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("main")).ConnectionString, Is.EqualTo("static-main"));
        });
    }

    [Test]
    public async Task UpdateAsyncReturnsNotAllowedForTheDefaultConnection()
    {
        ConnectionChangeResult result = await _host.Manager.UpdateAsync(TestHost.Record(string.Empty));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
    }

    [Test]
    public async Task UpdateAsyncKeepsThePreviousDefinitionWhenTheNewOneCannotBeOpened()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "old"));
        FakeConnectionHandlerService old = (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a");
        _host.Provider.TestFailure = options => options.ConnectionString == "unreachable" ? new InvalidOperationException("unreachable") : null;

        ConnectionChangeResult result = await _host.Manager.UpdateAsync(TestHost.Record("tenant-a", "unreachable"));

        await Task.Delay(100);
        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.TestFailed));
            Assert.That(await _host.Factory.GetHandlerAsync("tenant-a"), Is.SameAs(old));
            Assert.That(old.DisposeCount, Is.Zero);
        });
    }

    [Test]
    public async Task UpdateAsyncReturnsUnknownProviderAndInvalidLikeAdd()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        ConnectionChangeResult unknown = await _host.Manager.UpdateAsync(TestHost.Record("tenant-a", providerId: "oracle"));
        ConnectionChangeResult invalid = await _host.Manager.UpdateAsync(TestHost.Record("tenant-a", connectionString: string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Status, Is.EqualTo(ConnectionChangeStatus.UnknownProvider));
            Assert.That(invalid.Status, Is.EqualTo(ConnectionChangeStatus.Invalid));
        });
    }

    [Test]
    public async Task UpdateAsyncThrowsArgumentNullExceptionWhenTheRecordIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _host.Manager.UpdateAsync(null!));
    }

    #endregion Update

    #region Remove

    [Test]
    public async Task RemoveAsyncReturnsRemovedAndTheConnectionIsGone()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));
        FakeConnectionHandlerService loaded = (FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a");

        ConnectionChangeResult result = await _host.Manager.RemoveAsync("tenant-a");

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Removed));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _host.Factory.GetHandlerAsync("tenant-a"));
        Assert.That(await TestHost.WaitForAsync(() => loaded.DisposeCount == 1), Is.True, "the removed service was not disposed");
    }

    [Test]
    public async Task RemoveAsyncReturnsNotFoundWhenTheConnectionDoesNotExist()
    {
        ConnectionChangeResult result = await _host.Manager.RemoveAsync("tenant-a");

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotFound));
    }

    [TestCase("main")]
    [TestCase("")]
    public async Task RemoveAsyncReturnsNotAllowedForStaticConnections(string name)
    {
        ConnectionChangeResult result = await _host.Manager.RemoveAsync(name);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
            Assert.That(await _host.Factory.GetHandlerAsync(name), Is.Not.Null);
        });
    }

    [Test]
    public async Task RemoveAsyncAllowsTheNameToBeAddedAgain()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a", "first"));
        await _host.Manager.RemoveAsync("tenant-a");

        ConnectionChangeResult result = await _host.Manager.AddAsync(TestHost.Record("tenant-a", "second"));

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Added));
            Assert.That(((FakeConnectionHandlerService)await _host.Factory.GetHandlerAsync("tenant-a")).ConnectionString, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task RemoveAsyncThrowsArgumentNullExceptionWhenTheNameIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _host.Manager.RemoveAsync(null!));
    }

    #endregion Remove

    #region Names

    [Test]
    public async Task GetNamesAsyncListsEveryDynamicConnectionWhenTheStoreIsListable()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));
        await _host.Manager.AddAsync(TestHost.Record("tenant-b"));

        IReadOnlyCollection<string> names = await _host.Manager.GetNamesAsync();

        Assert.That(names, Is.EquivalentTo(["tenant-a", "tenant-b"]));
    }

    [Test]
    public async Task GetNamesAsyncListsOnlyTheLoadedConnectionsWhenTheStoreIsNotListable()
    {
        await using TestHost host = CreateHost(new NonListableConnectionStore());
        await host.Manager.AddAsync(TestHost.Record("tenant-a"));
        await host.Manager.AddAsync(TestHost.Record("tenant-b"));
        Assert.That(await host.Manager.GetNamesAsync(), Is.Empty);

        await host.Factory.GetHandlerAsync("tenant-a");

        Assert.That(await host.Manager.GetNamesAsync(), Is.EquivalentTo(["tenant-a"]));
    }

    [Test]
    public async Task GetNamesAsyncNeverListsTheStaticConnections()
    {
        await _host.Manager.AddAsync(TestHost.Record("tenant-a"));

        Assert.That(await _host.Manager.GetNamesAsync(), Does.Not.Contain("main"));
    }

    #endregion Names
}
