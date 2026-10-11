using Bacon.DBConnectionHandler.Models.Options;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionHandlerHealthCheckOptionsTests
{
    [Test]
    public void PropertiesDefaultToNull()
    {
        ConnectionHandlerHealthCheckOptions options = new();

        Assert.Multiple(() =>
        {
            Assert.That(options.Name, Is.Null);
            Assert.That(options.FailureStatus, Is.Null);
            Assert.That(options.HealthQuery, Is.Null);
            Assert.That(options.Timeout, Is.Null);
            Assert.That(options.Tags, Is.Null);
        });
    }

    [Test]
    public void ResolveNameReturnsProviderDefaultForTheDefaultConnection()
    {
        ConnectionHandlerHealthCheckOptions options = new();

        Assert.That(options.ResolveName(string.Empty, "sqlserver"), Is.EqualTo("sqlserver"));
    }

    [Test]
    public void ResolveNameSuffixesTheConnectionNameForANamedConnection()
    {
        ConnectionHandlerHealthCheckOptions options = new();

        Assert.That(options.ResolveName("reporting", "postgres"), Is.EqualTo("postgres:reporting"));
    }

    [TestCase("")]
    [TestCase("reporting")]
    public void ResolveNameReturnsTheExplicitNameAlways(string connectionName)
    {
        ConnectionHandlerHealthCheckOptions options = new() { Name = "my-check" };

        Assert.That(options.ResolveName(connectionName, "postgres"), Is.EqualTo("my-check"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ResolveNameIgnoresABlankName(string name)
    {
        ConnectionHandlerHealthCheckOptions options = new() { Name = name };

        Assert.That(options.ResolveName(string.Empty, "sqlite"), Is.EqualTo("sqlite"));
    }
}
