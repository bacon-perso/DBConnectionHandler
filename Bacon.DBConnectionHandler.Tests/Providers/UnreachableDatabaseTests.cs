using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Mssql.Extensions;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mysql.Extensions;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Postgres.Extensions;
using Bacon.DBConnectionHandler.Postgres.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The server providers must report a database that is not there as TestFailed, without needing a server.
/// Nothing listens on port 1, so each attempt is refused at once
/// </summary>
[TestFixture]
public class UnreachableDatabaseTests
{
    private static IEnumerable<TestCaseData> Unreachable()
    {
        yield return new TestCaseData(
            MssqlProviderDefaults.Provider,
            (ConnectionHandlerOptions)new MssqlConnectionHandlerOptions { ConnectionString = "Server=127.0.0.1,1;User Id=u;Password=p;Connect Timeout=1;ConnectRetryCount=0;Encrypt=False;Pooling=false" }).SetName("Mssql");
        yield return new TestCaseData(
            PostgresProviderDefaults.Provider,
            (ConnectionHandlerOptions)new PostgresConnectionHandlerOptions { ConnectionString = "Host=127.0.0.1;Port=1;Database=d;Username=u;Password=p;Timeout=1;Pooling=false" }).SetName("Postgres");
        yield return new TestCaseData(
            MysqlProviderDefaults.Provider,
            (ConnectionHandlerOptions)new MysqlConnectionHandlerOptions { ConnectionString = "Server=127.0.0.1;Port=1;Database=d;User=u;Password=p;ConnectionTimeout=1;Pooling=false" }).SetName("Mysql");
    }

    [TestCaseSource(nameof(Unreachable))]
    [CancelAfter(60000)]
    public async Task AddAsyncReturnsTestFailedAndStoresNothing(string provider, ConnectionHandlerOptions options, CancellationToken cancellationToken)
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddConnectionHandler().AddMssqlProvider().AddPostgresProvider().AddMysqlProvider().Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();

        ConnectionChangeResult result = await manager.AddAsync(new ConnectionRecord("tenant-a", provider, options), cancellationToken);

        Assert.Multiple(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.TestFailed));
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(await manager.GetNamesAsync(cancellationToken), Is.Empty);
        });
    }

    [TestCaseSource(nameof(Unreachable))]
    [CancelAfter(60000)]
    public async Task BuildThrowsAggregateExceptionForAStaticConnection(string provider, ConnectionHandlerOptions options, CancellationToken cancellationToken)
    {
        ServiceCollection services = new();
        ConnectionHandlerBuilder builder = services.AddConnectionHandler();
        AddStatic(builder, provider, options);

        AggregateException? exception = Assert.Throws<AggregateException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("tenant-a"));
        await Task.CompletedTask;
    }

    private static void AddStatic(ConnectionHandlerBuilder builder, string provider, ConnectionHandlerOptions options)
    {
        if (provider == MssqlProviderDefaults.Provider)
        {
            builder.AddMssql("tenant-a", o => o.ConnectionString = options.ConnectionString);
        }
        else if (provider == PostgresProviderDefaults.Provider)
        {
            builder.AddPostgres("tenant-a", o => o.ConnectionString = options.ConnectionString);
        }
        else
        {
            builder.AddMysql("tenant-a", o => o.ConnectionString = options.ConnectionString);
        }
    }
}
