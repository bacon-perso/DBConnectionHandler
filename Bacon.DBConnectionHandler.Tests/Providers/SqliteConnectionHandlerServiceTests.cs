using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Bacon.DBConnectionHandler.Sqlite.Services;
using Bacon.DBConnectionHandler.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The SQLite service against a real database file: no server is needed
/// </summary>
[TestFixture]
public class SqliteConnectionHandlerServiceTests
{
    private SqliteTestDatabase _database = null!;
    private SqliteConnectionHandlerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _database = new SqliteTestDatabase();
        _service = CreateService(_database.ConnectionString);
    }

    [TearDown]
    public void TearDown()
    {
        _database.Dispose();
    }

    private static SqliteConnectionHandlerService CreateService(string connectionString)
    {
        return new SqliteConnectionHandlerService(new SqliteConnectionHandlerOptions { ConnectionString = connectionString }, NullLogger<SqliteConnectionHandlerService>.Instance);
    }

    private async Task RunAsync(string sql, Dictionary<string, object?>? parameters = null)
    {
        await _service.ExecuteDbCommandNoReturnAsync(_service.CreateDbCommand(sql, CommandType.Text, parameters: parameters));
    }

    private async Task<DataTable> QueryAsync(string sql, Dictionary<string, object?>? parameters = null)
    {
        return (await _service.ExecuteDbCommandAsync(_service.CreateDbCommand(sql, CommandType.Text, parameters: parameters))).Single();
    }

    #region CreateDbCommand

    [Test]
    public void CreateDbCommandReturnsSqliteCommandWithTextTypeAndTimeout()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, commandTimeout: 120);

        Assert.Multiple(() =>
        {
            Assert.That(command, Is.InstanceOf<SqliteCommand>());
            Assert.That(command.CommandText, Is.EqualTo("SELECT 1"));
            Assert.That(command.CommandType, Is.EqualTo(CommandType.Text));
            Assert.That(command.CommandTimeout, Is.EqualTo(120));
        });
    }

    [Test]
    public void CreateDbCommandSetsDefaultCommandTimeout()
    {
        Assert.That(_service.CreateDbCommand("SELECT 1", CommandType.Text).CommandTimeout, Is.EqualTo(30));
    }

    [TestCase(CommandType.StoredProcedure)]
    [TestCase(CommandType.TableDirect)]
    public void CreateDbCommandThrowsArgumentExceptionWhenTheCommandTypeIsNotText(CommandType commandType)
    {
        Assert.Throws<ArgumentException>(() => _service.CreateDbCommand("SELECT 1", commandType));
    }

    [Test]
    public void CreateDbCommandAddsTheParametersAndTurnsNullIntoDBNull()
    {
        Dictionary<string, object?> parameters = new() { { "@a", 1 }, { "@b", null } };

        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, parameters: parameters);

        Assert.Multiple(() =>
        {
            Assert.That(command.Parameters, Has.Count.EqualTo(2));
            Assert.That(command.Parameters["@a"].Value, Is.EqualTo(1));
            Assert.That(command.Parameters["@b"].Value, Is.EqualTo(DBNull.Value));
        });
    }

    #endregion CreateDbCommand

    #region Commands

    [Test]
    public async Task ExecuteDbCommandCreatesInsertsAndReadsBack()
    {
        await RunAsync("create table people (id integer primary key, name text not null, age integer)");
        await RunAsync("insert into people (id, name, age) values (@id, @name, @age)", new() { { "@id", 1 }, { "@name", "Ada" }, { "@age", 36 } });
        await RunAsync("insert into people (id, name, age) values ($id, $name, $age)", new() { { "$id", 2 }, { "$name", "Linus" }, { "$age", null } });

        DataTable table = await QueryAsync("select id, name, age from people order by id");

        Assert.Multiple(() =>
        {
            Assert.That(table.Columns.Cast<DataColumn>().Select(c => c.ColumnName), Is.EqualTo(["id", "name", "age"]));
            Assert.That(table.Rows, Has.Count.EqualTo(2));
            Assert.That(table.Rows[0]["name"], Is.EqualTo("Ada"));
            Assert.That(table.Rows[0]["age"], Is.EqualTo(36L));
            Assert.That(table.Rows[1]["age"], Is.EqualTo(DBNull.Value));
        });
    }

    [Test]
    public async Task ExecuteDbCommandReturnsOneTablePerResultSet()
    {
        IEnumerable<DataTable> tables = await _service.ExecuteDbCommandAsync(_service.CreateDbCommand("select 1 as a; select 2 as b, 3 as c;", CommandType.Text));

        DataTable[] result = [.. tables];
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0].Columns[0].ColumnName, Is.EqualTo("a"));
            Assert.That(result[1].Columns, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteDbCommandReturnsAnEmptyTableWhenNothingMatches()
    {
        await RunAsync("create table people (id integer)");

        DataTable table = await QueryAsync("select id from people");

        Assert.Multiple(() =>
        {
            Assert.That(table.Rows, Is.Empty);
            Assert.That(table.Columns, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsyncRunsTheRoutineNameAsSqlText()
    {
        // SQLite has no routines: the text given as the routine name is executed as is
        IEnumerable<DataTable> tables = await _service.ExecuteAsync("select @value as v", new Dictionary<string, object?> { { "@value", 5 } });

        Assert.That(tables.Single().Rows[0]["v"], Is.EqualTo(5L));
    }

    [Test]
    public async Task ExecuteNoReturnAsyncRunsTheRoutineNameAsSqlText()
    {
        await _service.ExecuteNoReturnAsync("create table things (id integer)");

        Assert.That((await QueryAsync("select count(*) as n from things")).Rows[0]["n"], Is.EqualTo(0L));
    }

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsWhenTheSqlIsInvalid()
    {
        await Assert.ThrowsAsync<SqliteException>(async () => await QueryAsync("select from where"));
    }

    [Test]
    public async Task ExecuteDbCommandNoReturnAsyncThrowsWhenAConstraintIsViolated()
    {
        await RunAsync("create table people (id integer primary key)");
        await RunAsync("insert into people (id) values (1)");

        await Assert.ThrowsAsync<SqliteException>(async () => await RunAsync("insert into people (id) values (1)"));
    }

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsArgumentExceptionWhenTheCommandIsOfAnotherProvider()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandAsync(wrongCommand));
    }

    [Test]
    public async Task ExecuteDbCommandNoReturnAsyncThrowsArgumentExceptionWhenTheCommandIsOfAnotherProvider()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandNoReturnAsync(wrongCommand));
    }

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsSqliteExceptionWhenTheDatabaseFileDoesNotExist()
    {
        SqliteConnectionHandlerService service = CreateService(_database.MissingFileConnectionString);

        await Assert.ThrowsAsync<SqliteException>(async () => await service.ExecuteDbCommandAsync(service.CreateDbCommand("select 1", CommandType.Text)));
    }

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsOperationCanceledExceptionWhenTheTokenIsAlreadyCancelled()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        await cancellationTokenSource.CancelAsync();

        await Assert.CatchAsync<OperationCanceledException>(async () => await _service.ExecuteDbCommandAsync(_service.CreateDbCommand("select 1", CommandType.Text), cancellationTokenSource.Token));
    }

    #endregion Commands

    #region Bulk insert

    private static DataTable CreateItems(params (int Id, string Name)[] rows)
    {
        DataTable table = new();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));

        foreach ((int id, string name) in rows)
        {
            table.Rows.Add(id, name);
        }

        return table;
    }

    [Test]
    public async Task BulkInsertAsyncInsertsEveryRow()
    {
        await RunAsync("create table items (id integer primary key, name text)");
        using DataTable data = CreateItems((1, "one"), (2, "two"), (3, "three"));

        await _service.BulkInsertAsync(data, "items");

        DataTable table = await QueryAsync("select id, name from items order by id");
        Assert.Multiple(() =>
        {
            Assert.That(table.Rows, Has.Count.EqualTo(3));
            Assert.That(table.Rows[2]["name"], Is.EqualTo("three"));
        });
    }

    [Test]
    public async Task BulkInsertAsyncMapsTheColumnsAndInsertsOnlyTheMappedOnes()
    {
        await RunAsync("create table items (item_id integer primary key, item_name text)");
        using DataTable data = CreateItems((1, "one"));
        Dictionary<string, string> mapping = new() { { "id", "item_id" } };

        await _service.BulkInsertAsync(data, "items", mapping);

        DataTable table = await QueryAsync("select item_id, item_name from items");
        Assert.Multiple(() =>
        {
            Assert.That(table.Rows[0]["item_id"], Is.EqualTo(1L));
            Assert.That(table.Rows[0]["item_name"], Is.EqualTo(DBNull.Value));
        });
    }

    [Test]
    public async Task BulkInsertAsyncAcceptsASchemaQualifiedAndAQuotedTable()
    {
        await RunAsync("create table \"my items\" (id integer, name text)");
        using DataTable data = CreateItems((1, "one"));

        await _service.BulkInsertAsync(data, "main.\"my items\"");

        Assert.That((await QueryAsync("select count(*) as n from \"my items\"")).Rows[0]["n"], Is.EqualTo(1L));
    }

    [Test]
    public async Task BulkInsertAsyncStoresNullForDBNullValues()
    {
        await RunAsync("create table items (id integer, name text)");
        using DataTable data = CreateItems((1, "one"));
        data.Rows[0]["name"] = DBNull.Value;

        await _service.BulkInsertAsync(data, "items");

        Assert.That((await QueryAsync("select name from items")).Rows[0]["name"], Is.EqualTo(DBNull.Value));
    }

    [Test]
    public async Task BulkInsertAsyncRollsBackEveryRowWhenOneFails()
    {
        await RunAsync("create table items (id integer primary key, name text)");
        using DataTable data = CreateItems((1, "one"), (2, "two"), (2, "duplicate"));

        await Assert.ThrowsAsync<SqliteException>(async () => await _service.BulkInsertAsync(data, "items"));

        Assert.That((await QueryAsync("select count(*) as n from items")).Rows[0]["n"], Is.EqualTo(0L), "the rows before the failure were kept");
    }

    [Test]
    public async Task BulkInsertAsyncInsertsNothingForAnEmptyTable()
    {
        await RunAsync("create table items (id integer, name text)");
        using DataTable data = CreateItems();

        await _service.BulkInsertAsync(data, "items");

        Assert.That((await QueryAsync("select count(*) as n from items")).Rows[0]["n"], Is.EqualTo(0L));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenBulkOptionsAreGiven()
    {
        using DataTable data = CreateItems();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items", bulkInsertOptions: new MssqlBulkInsertOptions()));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentNullExceptionWhenTheDataIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _service.BulkInsertAsync(null!, "items"));
    }

    [TestCase("")]
    [TestCase("items; drop table users")]
    [TestCase("[items]")]
    [TestCase("a.b.c")]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenTheTableIsNotAnIdentifier(string table)
    {
        using DataTable data = CreateItems();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, table));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenAMappedColumnIsNotAnIdentifier()
    {
        using DataTable data = CreateItems((1, "one"));
        Dictionary<string, string> mapping = new() { { "id", "id); drop table items; --" } };

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items", mapping));
    }

    #endregion Bulk insert
}
