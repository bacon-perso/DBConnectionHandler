using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Tests.Fakes;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionRecordTests
{
    [Test]
    public void ToStringContainsNameAndProvider()
    {
        ConnectionRecord record = TestHost.Record("tenant-a", "Server=secret-host;Password=hunter2");

        string text = record.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("tenant-a"));
            Assert.That(text, Does.Contain(FakeConnectionProvider.DefaultProviderId));
        });
    }

    [Test]
    public void ToStringNeverContainsTheConnectionString()
    {
        ConnectionRecord record = TestHost.Record("tenant-a", "Server=secret-host;Password=hunter2");

        string text = record.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("hunter2"));
            Assert.That(text, Does.Not.Contain("secret-host"));
        });
    }

    [Test]
    public void EqualsComparesTheOptionsByReference()
    {
        FakeConnectionHandlerOptions options = new() { ConnectionString = "same" };

        ConnectionRecord first = new("a", "fake", options);
        ConnectionRecord second = new("a", "fake", options);
        ConnectionRecord third = new("a", "fake", new FakeConnectionHandlerOptions { ConnectionString = "same" });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(third));
        });
    }
}
