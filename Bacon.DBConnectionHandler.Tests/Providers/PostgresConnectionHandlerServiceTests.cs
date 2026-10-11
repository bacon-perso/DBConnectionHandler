using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Postgres.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The parts of the Postgres service that never reach the server
/// </summary>
[TestFixture]
public class PostgresConnectionHandlerServiceTests
{
    // Nothing listens on port 1: opening a connection fails at once
    private const string _unreachable = "Host=127.0.0.1;Port=1;Database=fakedb;Username=fakeuser;Password=fakepass;Timeout=1;Pooling=false";

    private PostgresConnectionHandlerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new PostgresConnectionHandlerService(new PostgresConnectionHandlerOptions { ConnectionString = "Host=fake-host;Database=fakedb;Username=fakeuser;Password=fakepass;" }, NullLogger<PostgresConnectionHandlerService>.Instance);
    }

    #region CreateDbCommand

    [Test]
    public void CreateDbCommandReturnsNpgsqlCommand()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command, Is.InstanceOf<NpgsqlCommand>());
    }

    [Test]
    public void CreateDbCommandSetsCommandText()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command.CommandText, Is.EqualTo("SELECT 1"));
    }

    [Test]
    public void CreateDbCommandSetsCommandType()
    {
        DbCommand command = _service.CreateDbCommand("my_function", CommandType.StoredProcedure);

        Assert.That(command.CommandType, Is.EqualTo(CommandType.StoredProcedure));
    }

    [Test]
    public void CreateDbCommandSetsDefaultCommandTimeout()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command.CommandTimeout, Is.EqualTo(30));
    }

    [Test]
    public void CreateDbCommandSetsCustomCommandTimeout()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, commandTimeout: 120);

        Assert.That(command.CommandTimeout, Is.EqualTo(120));
    }

    [Test]
    public void CreateDbCommandAddsParameters()
    {
        Dictionary<string, object?> parameters = new() { { "param1", "value1" } };

        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, parameters: parameters);

        Assert.That(command.Parameters["param1"].Value, Is.EqualTo("value1"));
    }

    [TestCase("@param1")]
    [TestCase(":param1")]
    public void CreateDbCommandRemovesTheParameterPrefix(string key)
    {
        Dictionary<string, object?> parameters = new() { { key, "value1" } };

        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, parameters: parameters);

        Assert.That(command.Parameters["param1"].Value, Is.EqualTo("value1"));
    }

    [Test]
    public void CreateDbCommandSetsDBNullWhenParameterValueIsNull()
    {
        Dictionary<string, object?> parameters = new() { { "param1", null } };

        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, parameters: parameters);

        Assert.That(command.Parameters["param1"].Value, Is.EqualTo(DBNull.Value));
    }

    [Test]
    public void CreateDbCommandHasNoParametersWhenParametersIsNull()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command.Parameters, Is.Empty);
    }

    [Test]
    public void CreateDbCommandAddsAllParameters()
    {
        Dictionary<string, object?> parameters = new()
        {
            { "param1", "value1" },
            { "param2", 42 },
            { "param3", null }
        };

        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text, parameters: parameters);

        Assert.That(command.Parameters, Has.Count.EqualTo(3));
    }

    #endregion CreateDbCommand

    #region Commands of another provider

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsArgumentExceptionWhenCommandIsNotNpgsqlCommand()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandAsync(wrongCommand));
    }

    [Test]
    public async Task ExecuteDbCommandNoReturnAsyncThrowsArgumentExceptionWhenCommandIsNotNpgsqlCommand()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandNoReturnAsync(wrongCommand));
    }

    #endregion Commands of another provider

    #region Bulk insert validation

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenTheOptionsAreOfAnotherProvider()
    {
        using DataTable data = new();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items", bulkInsertOptions: new MssqlBulkInsertOptions()));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentNullExceptionWhenTheDataIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _service.BulkInsertAsync(null!, "items"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("items; drop table users")]
    [TestCase("items--")]
    [TestCase("1items")]
    [TestCase("a.b.c.")]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenTheTableIsNotAnIdentifier(string table)
    {
        using DataTable data = new();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, table));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenAColumnIsNotAnIdentifier()
    {
        using DataTable data = new();
        data.Columns.Add("id; drop table users", typeof(int));

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items"));
    }

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenAMappedColumnIsNotAnIdentifier()
    {
        using DataTable data = new();
        data.Columns.Add("id", typeof(int));
        Dictionary<string, string> mapping = new() { { "id", "item_id\"; drop table users; --" } };

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items", mapping));
    }

    #endregion Bulk insert validation

    #region Opening a connection

    [Test]
    public async Task ExecuteAsyncThrowsNpgsqlExceptionWhenTheServerIsUnreachable()
    {
        PostgresConnectionHandlerService service = new(new PostgresConnectionHandlerOptions { ConnectionString = _unreachable }, NullLogger<PostgresConnectionHandlerService>.Instance);

        await Assert.ThrowsAsync<NpgsqlException>(async () => await service.ExecuteAsync("my_function"));
    }

    [Test]
    public async Task ExecuteNoReturnAsyncThrowsNpgsqlExceptionWhenTheServerIsUnreachable()
    {
        PostgresConnectionHandlerService service = new(new PostgresConnectionHandlerOptions { ConnectionString = _unreachable }, NullLogger<PostgresConnectionHandlerService>.Instance);

        await Assert.ThrowsAsync<NpgsqlException>(async () => await service.ExecuteNoReturnAsync("my_procedure"));
    }

    #endregion Opening a connection
}
