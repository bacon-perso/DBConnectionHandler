using Bacon.DBConnectionHandler.Tests.Fakes;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionHandlerOptionsTests
{
    [Test]
    public void ConnectionStringDefaultsToEmptyString()
    {
        FakeConnectionHandlerOptions options = new();

        Assert.That(options.ConnectionString, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ConnectionHandlerHealthCheckOptionsDefaultsToNull()
    {
        FakeConnectionHandlerOptions options = new();

        Assert.That(options.ConnectionHandlerHealthCheckOptions, Is.Null);
    }

    [Test]
    public void ValidatePassesWhenConnectionStringIsSet()
    {
        FakeConnectionHandlerOptions options = new() { ConnectionString = "Server=somewhere" };

        Assert.DoesNotThrow(options.Validate);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void ValidateThrowsArgumentExceptionWhenConnectionStringIsEmpty(string? connectionString)
    {
        FakeConnectionHandlerOptions options = new() { ConnectionString = connectionString! };

        ArgumentException? exception = Assert.Throws<ArgumentException>(options.Validate);

        Assert.That(exception!.ParamName, Is.EqualTo(nameof(FakeConnectionHandlerOptions.ConnectionString)));
    }
}
