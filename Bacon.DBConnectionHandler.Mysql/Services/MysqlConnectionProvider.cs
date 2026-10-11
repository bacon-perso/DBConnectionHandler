using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Bacon.DBConnectionHandler.Mysql.Services;

internal sealed class MysqlConnectionProvider : BaseConnectionProvider<MysqlConnectionHandlerOptions>
{
    public override string Provider => MysqlProviderDefaults.Provider;

    // MySqlConnector pools by connection string and expires idle connections by itself, so nothing needs disposing on retire
    protected override IConnectionHandlerService Create(string name, MysqlConnectionHandlerOptions mysqlConnectionHandlerOptions, IServiceProvider serviceProvider)
    {
        return new MysqlConnectionHandlerService(mysqlConnectionHandlerOptions, serviceProvider.GetRequiredService<ILogger<MysqlConnectionHandlerService>>());
    }

    protected override async Task TestAsync(MysqlConnectionHandlerOptions mysqlConnectionHandlerOptions, CancellationToken cancellationToken)
    {
        await using MySqlConnection mySqlConnection = new(mysqlConnectionHandlerOptions.ConnectionString);
        await mySqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}