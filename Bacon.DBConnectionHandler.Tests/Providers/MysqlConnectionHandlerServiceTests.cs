using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Mysql.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The parts of the MySQL service that never reach the server
/// </summary>
[TestFixture]
public class MysqlConnectionHandlerServiceTests
{
    private MysqlConnectionHandlerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        MysqlConnectionHandlerOptions options = new() { ConnectionString = "Server=fake-host;Database=fakedb;" };

        _service = new MysqlConnectionHandlerService(options, NullLogger<MysqlConnectionHandlerService>.Instance);
    }

    #region CreateDbCommand

    [Test]
    public void CreateDbCommandReturnsMySqlCommand()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command, Is.InstanceOf<MySqlCommand>());
    }

    [Test]
    public void CreateDbCommandSetsCommandTextTypeAndTimeout()
    {
        DbCommand command = _service.CreateDbCommand("my_procedure", CommandType.StoredProcedure, commandTimeout: 120);

        Assert.Multiple(() =>
        {
            Assert.That(command.CommandText, Is.EqualTo("my_procedure"));
            Assert.That(command.CommandType, Is.EqualTo(CommandType.StoredProcedure));
            Assert.That(command.CommandTimeout, Is.EqualTo(120));
        });
    }

    [Test]
    public void CreateDbCommandSetsDefaultCommandTimeout()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command.CommandTimeout, Is.EqualTo(30));
    }

    [TestCase("param1")]
    [TestCase("@param1")]
    public void CreateDbCommandNamesTheParameterWithAnAtPrefixOnce(string key)
    {
        Dictionary<string, object?> parameters = new() { { key, "value1" } };

        DbCommand command = _service.CreateDbCommand("my_procedure", CommandType.StoredProcedure, parameters: parameters);

        DbParameter parameter = command.Parameters.Cast<DbParameter>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(parameter.ParameterName, Is.EqualTo("@param1"));
            Assert.That(parameter.Value, Is.EqualTo("value1"));
        });
    }

    [Test]
    public void CreateDbCommandSetsDBNullWhenParameterValueIsNull()
    {
        Dictionary<string, object?> parameters = new() { { "param1", null } };

        DbCommand command = _service.CreateDbCommand("my_procedure", CommandType.StoredProcedure, parameters: parameters);

        Assert.That(command.Parameters.Cast<DbParameter>().Single().Value, Is.EqualTo(DBNull.Value));
    }

    [Test]
    public void CreateDbCommandHasNoParametersWhenParametersIsNull()
    {
        DbCommand command = _service.CreateDbCommand("my_procedure", CommandType.StoredProcedure);

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

        DbCommand command = _service.CreateDbCommand("my_procedure", CommandType.StoredProcedure, parameters: parameters);

        Assert.That(command.Parameters, Has.Count.EqualTo(3));
    }

    #endregion CreateDbCommand

    #region Commands of another provider

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsArgumentExceptionWhenCommandIsNotMySqlCommand()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandAsync(wrongCommand));
    }

    [Test]
    public async Task ExecuteDbCommandNoReturnAsyncThrowsArgumentExceptionWhenCommandIsNotMySqlCommand()
    {
        SqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandNoReturnAsync(wrongCommand));
    }

    #endregion Commands of another provider

    #region Bulk insert

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenBulkOptionsAreGiven()
    {
        using DataTable data = new();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "items", bulkInsertOptions: new MssqlBulkInsertOptions()));
    }

    #endregion Bulk insert
}
