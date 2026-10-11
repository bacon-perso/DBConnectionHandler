using Bacon.DBConnectionHandler.Models.Options;
using System.Text;

namespace Bacon.DBConnectionHandler.Models;

/// <summary>
/// The plain data that defines a dynamic connection. This is what a <see cref="Interfaces.Stores.IConnectionStore"/> keeps.
/// Treat <see cref="Options"/> as immutable once handed over, the in-memory store keeps the reference
/// </summary>
/// <param name="Name">The connection name. Cannot be empty, the default connection is always a static one</param>
/// <param name="Provider">The provider id (e.g. "mssql"). Selects the <see cref="Interfaces.Services.IConnectionProvider"/></param>
/// <param name="Options">The provider specific options, holding the connection string</param>
public sealed record ConnectionRecord(string Name, string Provider, ConnectionHandlerOptions Options)
{
    // The generated ToString would print every member. Only print what is safe to log, never the connection string
    private bool PrintMembers(StringBuilder stringBuilder)
    {
        stringBuilder.Append("Name = ").Append(Name).Append(", Provider = ").Append(Provider);

        return true;
    }
}