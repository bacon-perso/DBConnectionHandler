using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Stores;
using Bacon.DBConnectionHandler.Tests.Fakes;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class InMemoryConnectionHandlerStoreTests
{
    private InMemoryConnectionHandlerStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryConnectionHandlerStore();
    }

    [Test]
    public async Task GetAsyncReturnsNullWhenTheNameIsUnknown()
    {
        Assert.That(await _store.GetAsync("missing"), Is.Null);
    }

    [Test]
    public async Task SetAsyncThenGetAsyncReturnsTheRecord()
    {
        ConnectionRecord record = TestHost.Record("tenant-a");

        await _store.SetAsync(record);

        Assert.That(await _store.GetAsync("tenant-a"), Is.SameAs(record));
    }

    [Test]
    public async Task SetAsyncReplacesTheRecordWithTheSameName()
    {
        await _store.SetAsync(TestHost.Record("tenant-a", "first"));
        ConnectionRecord second = TestHost.Record("tenant-a", "second");

        await _store.SetAsync(second);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(await _store.GetAsync("tenant-a"), Is.SameAs(second));
            Assert.That(await _store.GetNamesAsync(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveAsyncReturnsTrueAndForgetsTheRecord()
    {
        await _store.SetAsync(TestHost.Record("tenant-a"));

        bool removed = await _store.RemoveAsync("tenant-a");

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(removed, Is.True);
            Assert.That(await _store.GetAsync("tenant-a"), Is.Null);
        });
    }

    [Test]
    public async Task RemoveAsyncReturnsFalseWhenTheNameIsUnknown()
    {
        Assert.That(await _store.RemoveAsync("missing"), Is.False);
    }

    [Test]
    public async Task GetNamesAsyncListsEveryStoredName()
    {
        await _store.SetAsync(TestHost.Record("a"));
        await _store.SetAsync(TestHost.Record("b"));

        IReadOnlyCollection<string> names = await _store.GetNamesAsync();

        Assert.That(names, Is.EquivalentTo(["a", "b"]));
    }

    [Test]
    public async Task GetNamesAsyncReturnsASnapshot()
    {
        await _store.SetAsync(TestHost.Record("a"));
        IReadOnlyCollection<string> names = await _store.GetNamesAsync();

        await _store.SetAsync(TestHost.Record("b"));

        Assert.That(names, Is.EquivalentTo(["a"]));
    }

    [Test]
    public void ItIsListable()
    {
        Assert.That(_store, Is.InstanceOf<Interfaces.Stores.IListableConnectionStore>());
    }
}
