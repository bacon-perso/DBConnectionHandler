using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Extensions;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mysql.Extensions;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Postgres.Extensions;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Sqlite.Extensions;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// What every provider package must do when it is registered. Nothing here opens a database
/// </summary>
[TestFixture]
public class ProviderRegistrationTests
{
    public delegate ConnectionHandlerBuilder AddProviderOnly(ConnectionHandlerBuilder builder);

    public delegate ConnectionHandlerBuilder AddStaticConnection(ConnectionHandlerBuilder builder, string name, string connectionString);

    private static IEnumerable<TestCaseData> ProviderOnly()
    {
        yield return new TestCaseData((AddProviderOnly)(b => b.AddMssqlProvider()), MssqlProviderDefaults.Provider, typeof(MssqlConnectionHandlerOptions)).SetName("Mssql");
        yield return new TestCaseData((AddProviderOnly)(b => b.AddPostgresProvider()), PostgresProviderDefaults.Provider, typeof(PostgresConnectionHandlerOptions)).SetName("Postgres");
        yield return new TestCaseData((AddProviderOnly)(b => b.AddSqliteProvider()), SqliteProviderDefaults.Provider, typeof(SqliteConnectionHandlerOptions)).SetName("Sqlite");
        yield return new TestCaseData((AddProviderOnly)(b => b.AddMysqlProvider()), MysqlProviderDefaults.Provider, typeof(MysqlConnectionHandlerOptions)).SetName("Mysql");
    }

    private static IEnumerable<TestCaseData> StaticConnection()
    {
        yield return new TestCaseData((AddStaticConnection)((b, name, cs) => b.AddMssql(name, o => o.ConnectionString = cs)), MssqlProviderDefaults.Provider, typeof(MssqlConnectionHandlerOptions)).SetName("Mssql");
        yield return new TestCaseData((AddStaticConnection)((b, name, cs) => b.AddPostgres(name, o => o.ConnectionString = cs)), PostgresProviderDefaults.Provider, typeof(PostgresConnectionHandlerOptions)).SetName("Postgres");
        yield return new TestCaseData((AddStaticConnection)((b, name, cs) => b.AddSqlite(name, o => o.ConnectionString = cs)), SqliteProviderDefaults.Provider, typeof(SqliteConnectionHandlerOptions)).SetName("Sqlite");
        yield return new TestCaseData((AddStaticConnection)((b, name, cs) => b.AddMysql(name, o => o.ConnectionString = cs)), MysqlProviderDefaults.Provider, typeof(MysqlConnectionHandlerOptions)).SetName("Mysql");
    }

    [TestCaseSource(nameof(ProviderOnly))]
    public void AddProviderRegistersAProviderWithItsIdAndOptionsType(AddProviderOnly add, string expectedProvider, Type expectedOptionsType)
    {
        ServiceCollection services = new();
        add(services.AddConnectionHandler());
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IConnectionProvider provider = serviceProvider.GetServices<IConnectionProvider>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Provider, Is.EqualTo(expectedProvider));
            Assert.That(provider.OptionsType, Is.EqualTo(expectedOptionsType));
            Assert.That(provider.OptionsType.IsAssignableTo(typeof(ConnectionHandlerOptions)), Is.True);
        });
    }

    [TestCaseSource(nameof(ProviderOnly))]
    public void AddProviderLetsBuildSucceedWithoutAnyStaticConnection(AddProviderOnly add, string expectedProvider, Type expectedOptionsType)
    {
        ServiceCollection services = new();

        Assert.DoesNotThrow(() => add(services.AddConnectionHandler()).Build());
    }

    [TestCaseSource(nameof(ProviderOnly))]
    public void AddProviderReturnsTheBuilder(AddProviderOnly add, string expectedProvider, Type expectedOptionsType)
    {
        ConnectionHandlerBuilder builder = new ServiceCollection().AddConnectionHandler();

        Assert.That(add(builder), Is.SameAs(builder));
    }

    [TestCaseSource(nameof(StaticConnection))]
    public void AddStaticConnectionCollectsTheConnectionWithItsRecord(AddStaticConnection add, string expectedProvider, Type expectedOptionsType)
    {
        ConnectionHandlerBuilder builder = new ServiceCollection().AddConnectionHandler();

        add(builder, "main", "Server=somewhere");

        ConnectionRecord record = builder.ConnectionHandles.Single().Record;
        Assert.Multiple(() =>
        {
            Assert.That(record.Name, Is.EqualTo("main"));
            Assert.That(record.Provider, Is.EqualTo(expectedProvider));
            Assert.That(record.Options, Is.InstanceOf(expectedOptionsType));
            Assert.That(record.Options.ConnectionString, Is.EqualTo("Server=somewhere"));
        });
    }

    [TestCaseSource(nameof(StaticConnection))]
    public void AddStaticConnectionAlsoRegistersTheProvider(AddStaticConnection add, string expectedProvider, Type expectedOptionsType)
    {
        ServiceCollection services = new();

        add(services.AddConnectionHandler(), "main", "Server=somewhere");

        Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IConnectionProvider)), Is.EqualTo(1));
    }

    [TestCaseSource(nameof(StaticConnection))]
    public void AddStaticConnectionThrowsArgumentExceptionWhenTheConnectionStringIsEmpty(AddStaticConnection add, string expectedProvider, Type expectedOptionsType)
    {
        ConnectionHandlerBuilder builder = new ServiceCollection().AddConnectionHandler();

        Assert.Throws<ArgumentException>(() => add(builder, "main", string.Empty));
    }

    [TestCaseSource(nameof(StaticConnection))]
    public void AddStaticConnectionThrowsInvalidOperationExceptionWhenTheNameIsAlreadyUsed(AddStaticConnection add, string expectedProvider, Type expectedOptionsType)
    {
        ConnectionHandlerBuilder builder = new ServiceCollection().AddConnectionHandler();
        add(builder, "main", "Server=somewhere");

        Assert.Throws<InvalidOperationException>(() => add(builder, "main", "Server=elsewhere"));
    }

    [Test]
    public void AddStaticConnectionAcceptsADefaultConnectionThroughTheOverloadWithoutAName()
    {
        ConnectionHandlerBuilder builder = new ServiceCollection().AddConnectionHandler();

        builder.AddMssql(o => o.ConnectionString = "Server=a");

        Assert.That(builder.ConnectionHandles.Single().Record.Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ProvidersCanAllBeRegisteredTogether()
    {
        ServiceCollection services = new();
        services.AddConnectionHandler().AddMssqlProvider().AddPostgresProvider().AddSqliteProvider().AddMysqlProvider().Build();
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        string[] ids = [.. serviceProvider.GetServices<IConnectionProvider>().Select(p => p.Provider)];

        Assert.That(ids, Is.EquivalentTo(["mssql", "postgres", "sqlite", "mysql"]));
    }
}
