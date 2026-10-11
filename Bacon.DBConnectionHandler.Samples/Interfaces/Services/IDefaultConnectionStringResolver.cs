using Bacon.DBConnectionHandler.Models.Options;

namespace Bacon.DBConnectionHandler.Samples.Interfaces.Services;

public interface IDefaultConnectionStringResolver
{
    ConnectionHandlerOptions? GetOption(string provider);

    Type? GetOptionType(string provider);
}