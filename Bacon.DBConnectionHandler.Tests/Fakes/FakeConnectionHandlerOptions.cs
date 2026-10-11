using Bacon.DBConnectionHandler.Models.Options;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// Options of <see cref="FakeConnectionProvider"/>
/// </summary>
internal sealed class FakeConnectionHandlerOptions : ConnectionHandlerOptions
{
}

/// <summary>
/// Options of another provider, used to check that a provider refuses options that are not its own
/// </summary>
internal sealed class OtherFakeConnectionHandlerOptions : ConnectionHandlerOptions
{
}
