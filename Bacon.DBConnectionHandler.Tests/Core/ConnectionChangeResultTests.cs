using Bacon.DBConnectionHandler.Models;

namespace Bacon.DBConnectionHandler.Tests.Core;

[TestFixture]
public class ConnectionChangeResultTests
{
    [TestCase(ConnectionChangeStatus.Added, true)]
    [TestCase(ConnectionChangeStatus.Updated, true)]
    [TestCase(ConnectionChangeStatus.Removed, true)]
    [TestCase(ConnectionChangeStatus.NotFound, false)]
    [TestCase(ConnectionChangeStatus.AlreadyExists, false)]
    [TestCase(ConnectionChangeStatus.NotAllowed, false)]
    [TestCase(ConnectionChangeStatus.Invalid, false)]
    [TestCase(ConnectionChangeStatus.UnknownProvider, false)]
    [TestCase(ConnectionChangeStatus.TestFailed, false)]
    public void SucceededIsTrueOnlyWhenTheChangeWasApplied(ConnectionChangeStatus status, bool expected)
    {
        ConnectionChangeResult result = new("tenant", status);

        Assert.That(result.Succeeded, Is.EqualTo(expected));
    }

    [Test]
    public void ErrorDefaultsToNull()
    {
        ConnectionChangeResult result = new("tenant", ConnectionChangeStatus.Added);

        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void EveryStatusIsCoveredByTheSucceededTest()
    {
        // Adding a status without deciding whether it is a success must fail here
        int covered = typeof(ConnectionChangeResultTests)
            .GetMethod(nameof(SucceededIsTrueOnlyWhenTheChangeWasApplied))!
            .GetCustomAttributes(typeof(TestCaseAttribute), inherit: false)
            .Length;

        Assert.That(covered, Is.EqualTo(Enum.GetValues<ConnectionChangeStatus>().Length));
    }
}
