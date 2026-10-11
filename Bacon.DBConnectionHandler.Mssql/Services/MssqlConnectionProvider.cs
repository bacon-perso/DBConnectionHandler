using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Mssql.Extensions;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bacon.DBConnectionHandler.Mssql.Services;

internal sealed class MssqlConnectionProvider : BaseConnectionProvider<MssqlConnectionHandlerOptions>
{
    public override string Provider => MssqlProviderDefaults.Provider;

    // SqlClient pools by connection string and expires idle connections by itself, so nothing needs disposing on retire
    protected override IConnectionHandlerService Create(string name, MssqlConnectionHandlerOptions mssqlConnectionHandlerOptions, IServiceProvider serviceProvider)
    {
        return new MssqlConnectionHandlerService(mssqlConnectionHandlerOptions, MssqlConnectionHandlerExtensions.UseAsyncRead(), serviceProvider.GetRequiredService<ILogger<MssqlConnectionHandlerService>>());
    }

    protected override async Task TestAsync(MssqlConnectionHandlerOptions mssqlConnectionHandlerOptions, CancellationToken cancellationToken)
    {
        await using SqlConnection sqlConnection = new(mssqlConnectionHandlerOptions.ConnectionString);
        await sqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}