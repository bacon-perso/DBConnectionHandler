using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Samples.Interfaces.Services;
using StackExchange.Redis;
using System.Net;
using System.Text.Json;

namespace Bacon.DBConnectionHandler.Samples.Services.Stores;

public class CustomStore(ConnectionMultiplexer connectionMultiplexer, IDatabase database, IDefaultConnectionStringResolver defaultConnectionStringResolver) : IListableConnectionStore
{
    private readonly static string _keyPrefix = "connectionhandler:dynamic:";

    public async ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Error while getting redis item", null, CommandStatus.Unknown);
        }

        RedisValue redisValue = await database.StringGetAsync(GetKey(name), CommandFlags.None);

        if (!redisValue.HasValue)
        {
            return default;
        }

        using JsonDocument document = JsonDocument.Parse(redisValue.ToString());
        JsonElement root = document.RootElement;

        string? provider = root.GetProperty(nameof(ConnectionRecord.Provider)).GetString();
        if (string.IsNullOrEmpty(provider))
        {
            throw new InvalidOperationException($"DB - No provider '{provider}' is registered for the stored connection '{name}'");
        }

        Type type = defaultConnectionStringResolver.GetOptionType(provider) ?? throw new InvalidOperationException($"DB - No provider '{provider}' is registered for the stored connection '{name}'");

        ConnectionHandlerOptions options = (ConnectionHandlerOptions)root.GetProperty(nameof(ConnectionRecord.Options)).Deserialize(type)!;

        //real store would decrypt here

        return new ConnectionRecord(name, provider, options);
    }

    public async ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default)
    {
        EndPoint endPoint = connectionMultiplexer.GetEndPoints().First();

        RedisKey[] redisKeys = await connectionMultiplexer.GetServer(endPoint).KeysAsync(pattern: _keyPrefix + "*").ToArrayAsync(cancellationToken: cancellationToken);

        return redisKeys.Select(s => s.ToString().Replace(_keyPrefix, "")).ToArray().AsReadOnly();
    }

    public async ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Error while setting redis item", null, CommandStatus.Unknown);
        }

        //real store would encrypt here

        await database.StringSetAsync(GetKey(connectionRecord.Name), JsonSerializer.Serialize(connectionRecord), TimeSpan.FromSeconds(60));
    }

    public async ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!connectionMultiplexer.IsConnected)
        {
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Error while removing redis item", null, CommandStatus.Unknown);
        }

        return await database.KeyDeleteAsync(GetKey(name));
    }

    private static string GetKey(string name) => _keyPrefix + name;
}