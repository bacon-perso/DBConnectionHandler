using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Sqlite.Extensions;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The whole library, registration to query, over real SQLite files
/// </summary>
[TestFixture]
public class SqliteIntegrationTests
{
    private readonly List<SqliteTestDatabase> _databases = [];

    [TearDown]
    public void TearDown()
    {
        foreach (SqliteTestDatabase database in _databases)
        {
            database.Dispose();
        }

        _databases.Clear();
    }

    private SqliteTestDatabase NewDatabase()
    {
        SqliteTestDatabase database = new();
        _databases.Add(database);

        return database;
    }

    private static ServiceCollection NewServices()
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOptions();

        return services;
    }

    private static ConnectionRecord SqliteRecord(string name, string connectionString)
    {
        return new ConnectionRecord(name, SqliteProviderDefaults.Provider, new SqliteConnectionHandlerOptions { ConnectionString = connectionString });
    }

    private static async Task RunAsync(IConnectionHandlerService handler, string sql, Dictionary<string, object?>? parameters = null)
    {
        await handler.ExecuteDbCommandNoReturnAsync(handler.CreateDbCommand(sql, CommandType.Text, parameters: parameters));
    }

    private static async Task<object?> ScalarAsync(IConnectionHandlerService handler, string sql)
    {
        DataTable table = (await handler.ExecuteDbCommandAsync(handler.CreateDbCommand(sql, CommandType.Text))).Single();

        return table.Rows[0][0];
    }

    #region Static connections

    [Test]
    public async Task BuildOpensTheStaticConnectionAndItCanBeUsed()
    {
        SqliteTestDatabase database = NewDatabase();
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqlite("db", o => o.ConnectionString = database.ConnectionString).Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IConnectionHandlerService handler = await serviceProvider.GetRequiredService<IConnectionHandlerFactory>().GetHandlerAsync("db");

        Assert.That(await ScalarAsync(handler, "select 41 + 1"), Is.EqualTo(42L));
    }

    [Test]
    public async Task BuildExposesTheDefaultConnectionAsAPlainService()
    {
        SqliteTestDatabase database = NewDatabase();
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqlite(o => o.ConnectionString = database.ConnectionString).Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IConnectionHandlerService handler = serviceProvider.GetRequiredService<IConnectionHandlerService>();

        Assert.That(await ScalarAsync(handler, "select 1"), Is.EqualTo(1L));
    }

    [Test]
    public void BuildThrowsAggregateExceptionWhenTheDatabaseCannotBeOpened()
    {
        SqliteTestDatabase database = NewDatabase();
        ServiceCollection services = NewServices();
        ConnectionHandlerBuilder builder = services.AddConnectionHandler().AddSqlite("db", o => o.ConnectionString = database.MissingFileConnectionString);

        AggregateException? exception = Assert.Throws<AggregateException>(() => builder.Build());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("db"));
            Assert.That(exception.InnerExceptions.Single().InnerException, Is.InstanceOf<SqliteException>());
            Assert.That(services.Any(descriptor => descriptor.IsKeyedService), Is.False);
        });
    }

    #endregion Static connections

    #region Health checks

    private static async Task<HealthCheckRegistration[]> GetRegistrationsAsync(Action<ConnectionHandlerBuilder> configure)
    {
        ServiceCollection services = NewServices();
        configure(services.AddConnectionHandler());
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        return [.. serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    [Test]
    public async Task HealthCheckIsNotRegisteredWhenTheOptionsAreNull()
    {
        SqliteTestDatabase database = NewDatabase();

        HealthCheckRegistration[] registrations = await GetRegistrationsAsync(b => b.AddSqlite("db", o => o.ConnectionString = database.ConnectionString).Build());

        Assert.That(registrations, Is.Empty);
    }

    [Test]
    public async Task HealthCheckUsesTheProviderDefaults()
    {
        SqliteTestDatabase database = NewDatabase();

        HealthCheckRegistration[] registrations = await GetRegistrationsAsync(b => b
            .AddSqlite("db", o =>
            {
                o.ConnectionString = database.ConnectionString;
                o.ConnectionHandlerHealthCheckOptions = new ConnectionHandlerHealthCheckOptions();
            })
            .Build());

        HealthCheckRegistration registration = registrations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(registration.Name, Is.EqualTo("sqlite:db"));
            Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(registration.Tags, Is.EquivalentTo(["database"]));
        });
    }

    [Test]
    public async Task HealthCheckNamesTheDefaultConnectionWithoutASuffix()
    {
        SqliteTestDatabase database = NewDatabase();

        HealthCheckRegistration[] registrations = await GetRegistrationsAsync(b => b
            .AddSqlite(o =>
            {
                o.ConnectionString = database.ConnectionString;
                o.ConnectionHandlerHealthCheckOptions = new ConnectionHandlerHealthCheckOptions();
            })
            .Build());

        Assert.That(registrations.Single().Name, Is.EqualTo("sqlite"));
    }

    [Test]
    public async Task HealthCheckHonorsTheConfiguredValues()
    {
        SqliteTestDatabase database = NewDatabase();

        HealthCheckRegistration[] registrations = await GetRegistrationsAsync(b => b
            .AddSqlite("db", o =>
            {
                o.ConnectionString = database.ConnectionString;
                o.ConnectionHandlerHealthCheckOptions = new ConnectionHandlerHealthCheckOptions
                {
                    Name = "main-database",
                    FailureStatus = HealthStatus.Degraded,
                    Tags = ["custom"],
                    Timeout = TimeSpan.FromSeconds(7)
                };
            })
            .Build());

        HealthCheckRegistration registration = registrations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(registration.Name, Is.EqualTo("main-database"));
            Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Degraded));
            Assert.That(registration.Tags, Is.EquivalentTo(["custom"]));
            Assert.That(registration.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
        });
    }

    [Test]
    public async Task HealthCheckReportsHealthyForAReachableDatabase()
    {
        SqliteTestDatabase database = NewDatabase();
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqlite("db", o =>
        {
            o.ConnectionString = database.ConnectionString;
            o.ConnectionHandlerHealthCheckOptions = new ConnectionHandlerHealthCheckOptions();
        }).Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        HealthReport report = await serviceProvider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(report.Entries, Contains.Key("sqlite:db"));
        });
    }

    #endregion Health checks

    #region Dynamic connections

    private static async Task<ServiceProvider> BuildDynamicAsync()
    {
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqliteProvider().ConfigureDynamicConnections(o => o.RetireDelay = TimeSpan.Zero).Build();

        return await Task.FromResult(services.BuildServiceProvider());
    }

    [Test]
    public async Task ManagerAddsATenantThatCanBeQueried()
    {
        SqliteTestDatabase database = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();

        ConnectionChangeResult result = await serviceProvider.GetRequiredService<IConnectionHandlerManager>().AddAsync(SqliteRecord("tenant-a", database.ConnectionString));

        IConnectionHandlerService handler = await serviceProvider.GetRequiredService<IConnectionHandlerFactory>().GetHandlerAsync("tenant-a");
        Assert.Multiple(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Added));
            Assert.That(await ScalarAsync(handler, "select 7"), Is.EqualTo(7L));
        });
    }

    [Test]
    public async Task ManagerKeepsTenantsIsolated()
    {
        SqliteTestDatabase first = NewDatabase();
        SqliteTestDatabase second = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();
        IConnectionHandlerFactory factory = serviceProvider.GetRequiredService<IConnectionHandlerFactory>();
        await manager.AddAsync(SqliteRecord("tenant-a", first.ConnectionString));
        await manager.AddAsync(SqliteRecord("tenant-b", second.ConnectionString));

        await RunAsync(await factory.GetHandlerAsync("tenant-a"), "create table notes (text text)");
        await RunAsync(await factory.GetHandlerAsync("tenant-a"), "insert into notes values ('only in a')");
        await RunAsync(await factory.GetHandlerAsync("tenant-b"), "create table notes (text text)");

        Assert.Multiple(async () =>
        {
            Assert.That(await ScalarAsync(await factory.GetHandlerAsync("tenant-a"), "select count(*) from notes"), Is.EqualTo(1L));
            Assert.That(await ScalarAsync(await factory.GetHandlerAsync("tenant-b"), "select count(*) from notes"), Is.EqualTo(0L));
        });
    }

    [Test]
    public async Task ManagerUpdatesATenantToAnotherDatabase()
    {
        SqliteTestDatabase before = NewDatabase();
        SqliteTestDatabase after = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();
        IConnectionHandlerFactory factory = serviceProvider.GetRequiredService<IConnectionHandlerFactory>();
        await manager.AddAsync(SqliteRecord("tenant-a", before.ConnectionString));
        await RunAsync(await factory.GetHandlerAsync("tenant-a"), "create table marker (name text)");
        await RunAsync(await factory.GetHandlerAsync("tenant-a"), "insert into marker values ('before')");

        ConnectionChangeResult result = await manager.UpdateAsync(SqliteRecord("tenant-a", after.ConnectionString));

        IConnectionHandlerService handler = await factory.GetHandlerAsync("tenant-a");
        await RunAsync(handler, "create table marker (name text)");
        await RunAsync(handler, "insert into marker values ('after')");
        Assert.Multiple(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Updated));
            Assert.That(await ScalarAsync(handler, "select name from marker"), Is.EqualTo("after"));
        });
    }

    [Test]
    public async Task ManagerKeepsTheTenantWhenTheUpdateCannotBeOpened()
    {
        SqliteTestDatabase working = NewDatabase();
        SqliteTestDatabase broken = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();
        await manager.AddAsync(SqliteRecord("tenant-a", working.ConnectionString));

        ConnectionChangeResult result = await manager.UpdateAsync(SqliteRecord("tenant-a", broken.MissingFileConnectionString));

        IConnectionHandlerService handler = await serviceProvider.GetRequiredService<IConnectionHandlerFactory>().GetHandlerAsync("tenant-a");
        Assert.Multiple(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.TestFailed));
            Assert.That(result.Error!.InnerException, Is.InstanceOf<SqliteException>());
            Assert.That(await ScalarAsync(handler, "select 1"), Is.EqualTo(1L));
        });
    }

    [Test]
    public async Task ManagerRemovesATenant()
    {
        SqliteTestDatabase database = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();
        await manager.AddAsync(SqliteRecord("tenant-a", database.ConnectionString));

        ConnectionChangeResult result = await manager.RemoveAsync("tenant-a");

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Removed));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await serviceProvider.GetRequiredService<IConnectionHandlerFactory>().GetHandlerAsync("tenant-a"));
    }

    [Test]
    public async Task ManagerReturnsTestFailedForADatabaseThatCannotBeOpened()
    {
        SqliteTestDatabase database = NewDatabase();
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();

        ConnectionChangeResult result = await manager.AddAsync(SqliteRecord("tenant-a", database.MissingFileConnectionString));

        Assert.Multiple(async () =>
        {
            Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.TestFailed));
            Assert.That(await manager.GetNamesAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task ManagerReturnsUnknownProviderWhenTheProviderWasNeverRegistered()
    {
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();
        ConnectionRecord record = new("tenant-a", "mssql", new SqliteConnectionHandlerOptions { ConnectionString = "x" });

        ConnectionChangeResult result = await serviceProvider.GetRequiredService<IConnectionHandlerManager>().AddAsync(record);

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.UnknownProvider));
    }

    [Test]
    public async Task ManagerReturnsInvalidWhenTheConnectionStringIsEmpty()
    {
        await using ServiceProvider serviceProvider = await BuildDynamicAsync();

        ConnectionChangeResult result = await serviceProvider.GetRequiredService<IConnectionHandlerManager>().AddAsync(SqliteRecord("tenant-a", string.Empty));

        Assert.That(result.Status, Is.EqualTo(ConnectionChangeStatus.Invalid));
    }

    [Test]
    public async Task ManagerRefusesToTouchTheStaticConnections()
    {
        SqliteTestDatabase staticDatabase = NewDatabase();
        SqliteTestDatabase other = NewDatabase();
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqlite("static", o => o.ConnectionString = staticDatabase.ConnectionString).Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IConnectionHandlerManager manager = serviceProvider.GetRequiredService<IConnectionHandlerManager>();

        ConnectionChangeResult add = await manager.AddAsync(SqliteRecord("static", other.ConnectionString));
        ConnectionChangeResult update = await manager.UpdateAsync(SqliteRecord("static", other.ConnectionString));
        ConnectionChangeResult remove = await manager.RemoveAsync("static");

        Assert.Multiple(() =>
        {
            Assert.That(add.Status, Is.EqualTo(ConnectionChangeStatus.AlreadyExists));
            Assert.That(update.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
            Assert.That(remove.Status, Is.EqualTo(ConnectionChangeStatus.NotAllowed));
        });
    }

    [Test]
    public async Task StaticAndDynamicConnectionsWorkSideBySide()
    {
        SqliteTestDatabase staticDatabase = NewDatabase();
        SqliteTestDatabase tenantDatabase = NewDatabase();
        ServiceCollection services = NewServices();
        services.AddConnectionHandler().AddSqlite(o => o.ConnectionString = staticDatabase.ConnectionString).Build();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await serviceProvider.GetRequiredService<IConnectionHandlerManager>().AddAsync(SqliteRecord("tenant-a", tenantDatabase.ConnectionString));
        IConnectionHandlerFactory factory = serviceProvider.GetRequiredService<IConnectionHandlerFactory>();

        await RunAsync(serviceProvider.GetRequiredService<IConnectionHandlerService>(), "create table only_static (id integer)");

        Assert.Multiple(async () =>
        {
            Assert.That(await ScalarAsync(await factory.GetHandlerAsync(), "select count(*) from sqlite_master where name = 'only_static'"), Is.EqualTo(1L));
            Assert.That(await ScalarAsync(await factory.GetHandlerAsync("tenant-a"), "select count(*) from sqlite_master where name = 'only_static'"), Is.EqualTo(0L));
            Assert.That(factory.Names, Is.EquivalentTo([string.Empty, "tenant-a"]));
        });
    }

    #endregion Dynamic connections
}
