using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Samples.Interfaces.Services;
using Bacon.DBConnectionHandler.Samples.Models;
using Bacon.DBConnectionHandler.Sqlite.Models;

namespace Bacon.DBConnectionHandler.Samples.Services;

internal sealed class DefaultConnectionStringResolver(DefaultConnectionStrings defaultConnectionStrings) : IDefaultConnectionStringResolver
{
    public ConnectionHandlerOptions? GetOption(string provider)
    {
        return provider switch
        {
            MssqlProviderDefaults.Provider => new MssqlConnectionHandlerOptions() { ConnectionString = defaultConnectionStrings.MssqlConnectionString },
            PostgresProviderDefaults.Provider => new PostgresConnectionHandlerOptions() { ConnectionString = defaultConnectionStrings.PostgresConnectionString },
            MysqlProviderDefaults.Provider => new MysqlConnectionHandlerOptions() { ConnectionString = defaultConnectionStrings.MysqlConnectionString },
            SqliteProviderDefaults.Provider => new SqliteConnectionHandlerOptions() { ConnectionString = defaultConnectionStrings.SqliteConnectionString },
            _ => null
        };
    }

    public Type? GetOptionType(string provider)
    {
        return provider switch
        {
            MssqlProviderDefaults.Provider => typeof(MssqlConnectionHandlerOptions),
            PostgresProviderDefaults.Provider => typeof(PostgresConnectionHandlerOptions),
            MysqlProviderDefaults.Provider => typeof(MysqlConnectionHandlerOptions),
            SqliteProviderDefaults.Provider => typeof(SqliteConnectionHandlerOptions),
            _ => null
        };
    }
}