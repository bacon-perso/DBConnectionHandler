using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bacon.DBConnectionHandler.Sqlite.Services;

internal sealed class SqliteConnectionProvider : BaseConnectionProvider<SqliteConnectionHandlerOptions>
{
    public override string Provider => SqliteProviderDefaults.Provider;

    protected override IConnectionHandlerService Create(string name, SqliteConnectionHandlerOptions sqliteConnectionHandlerOptions, IServiceProvider serviceProvider)
    {
        return new SqliteConnectionHandlerService(sqliteConnectionHandlerOptions, serviceProvider.GetRequiredService<ILogger<SqliteConnectionHandlerService>>());
    }

    // Microsoft.Data.Sqlite runs its async methods synchronously, so this is a plain open
    protected override Task TestAsync(SqliteConnectionHandlerOptions sqliteConnectionHandlerOptions, CancellationToken cancellationToken)
    {
        using SqliteConnection sqliteConnection = new(sqliteConnectionHandlerOptions.ConnectionString);
        sqliteConnection.Open();

        return Task.CompletedTask;
    }
}