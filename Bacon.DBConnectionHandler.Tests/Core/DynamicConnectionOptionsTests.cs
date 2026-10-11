using Bacon.DBConnectionHandler.Models.Options;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class DynamicConnectionOptionsTests
{
    [Test]
    public void CacheDurationDefaultsToNull()
    {
        DynamicConnectionOptions options = new();

        Assert.That(options.CacheDuration, Is.Null);
    }

    [Test]
    public void RetireDelayDefaultsToThirtySeconds()
    {
        DynamicConnectionOptions options = new();

        Assert.That(options.RetireDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }
}
