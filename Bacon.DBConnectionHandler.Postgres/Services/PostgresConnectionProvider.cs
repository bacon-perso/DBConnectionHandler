using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Bacon.DBConnectionHandler.Postgres.Services;

internal sealed class PostgresConnectionProvider : BaseConnectionProvider<PostgresConnectionHandlerOptions>
{
    public override string Provider => PostgresProviderDefaults.Provider;

    // Npgsql pools by connection string and expires idle connections by itself, so nothing needs disposing on retire
    protected override IConnectionHandlerService Create(string name, PostgresConnectionHandlerOptions postgresConnectionHandlerOptions, IServiceProvider serviceProvider)
    {
        return new PostgresConnectionHandlerService(postgresConnectionHandlerOptions, serviceProvider.GetRequiredService<ILogger<PostgresConnectionHandlerService>>());
    }

    protected override async Task TestAsync(PostgresConnectionHandlerOptions postgresConnectionHandlerOptions, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection npgsqlConnection = new(postgresConnectionHandlerOptions.ConnectionString);
        await npgsqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}