using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Tests.Fakes;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class BaseConnectionProviderTests
{
    private FakeConnectionProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new FakeConnectionProvider();
    }

    [Test]
    public void OptionsTypeIsTheTypedOptions()
    {
        Assert.That(_provider.OptionsType, Is.EqualTo(typeof(FakeConnectionHandlerOptions)));
    }

    [Test]
    public void ProviderIsTheProviderId()
    {
        Assert.That(new FakeConnectionProvider("custom").Provider, Is.EqualTo("custom"));
    }

    [Test]
    public void CreatePassesTheTypedOptionsToTheProvider()
    {
        IConnectionHandlerService service = _provider.Create(TestHost.Record("a", "the-string"), null!);

        Assert.That(((FakeConnectionHandlerService)service).ConnectionString, Is.EqualTo("the-string"));
    }

    [Test]
    public void CreateThrowsArgumentExceptionWhenTheOptionsAreOfAnotherProvider()
    {
        ConnectionRecord record = new("a", "fake", new OtherFakeConnectionHandlerOptions { ConnectionString = "x" });

        Assert.Throws<ArgumentException>(() => _provider.Create(record, null!));
    }

    [Test]
    public async Task TestAsyncRunsTheProviderTest()
    {
        await _provider.TestAsync(TestHost.Record("a"), CancellationToken.None);

        Assert.That(_provider.TestCount, Is.EqualTo(1));
    }

    [Test]
    public void TestAsyncThrowsArgumentExceptionWhenTheOptionsAreOfAnotherProvider()
    {
        ConnectionRecord record = new("a", "fake", new OtherFakeConnectionHandlerOptions { ConnectionString = "x" });

        Assert.Throws<ArgumentException>(() => _provider.TestAsync(record, CancellationToken.None));
    }
}
