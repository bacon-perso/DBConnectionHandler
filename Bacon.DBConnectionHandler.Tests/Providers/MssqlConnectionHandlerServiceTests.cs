using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mssql.Services;
using Bacon.DBConnectionHandler.Postgres.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Providers;

/// <summary>
/// The parts of the MSSQL service that never reach the server
/// </summary>
[TestFixture]
public class MssqlConnectionHandlerServiceTests
{
    private MssqlConnectionHandlerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        MssqlConnectionHandlerOptions options = new() { ConnectionString = "Server=fake-host;Database=fakedb;" };

        _service = new MssqlConnectionHandlerService(options, useAsyncRead: false, NullLogger<MssqlConnectionHandlerService>.Instance);
    }

    #region CreateDbCommand

    [Test]
    public void CreateDbCommandReturnsSqlCommand()
    {
        DbCommand command = _service.CreateDbCommand("SELECT 1", CommandType.Text);

        Assert.That(command, Is.InstanceOf<SqlCommand>());
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
        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure);

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
    public void CreateDbCommandAddsAtPrefixWhenParameterNameHasNoPrefix()
    {
        Dictionary<string, object?> parameters = new() { { "param1", "value1" } };

        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure, parameters: parameters);

        Assert.That(command.Parameters["@param1"].Value, Is.EqualTo("value1"));
    }

    [Test]
    public void CreateDbCommandPreservesAtPrefixWhenParameterNameAlreadyHasIt()
    {
        Dictionary<string, object?> parameters = new() { { "@param1", "value1" } };

        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure, parameters: parameters);

        Assert.Multiple(() =>
        {
            Assert.That(command.Parameters["@param1"].Value, Is.EqualTo("value1"));
            Assert.That(command.Parameters, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void CreateDbCommandSetsDBNullWhenParameterValueIsNull()
    {
        Dictionary<string, object?> parameters = new() { { "param1", null } };

        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure, parameters: parameters);

        Assert.That(command.Parameters["@param1"].Value, Is.EqualTo(DBNull.Value));
    }

    [Test]
    public void CreateDbCommandHasNoParametersWhenParametersIsNull()
    {
        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure);

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

        DbCommand command = _service.CreateDbCommand("sp_test", CommandType.StoredProcedure, parameters: parameters);

        Assert.That(command.Parameters, Has.Count.EqualTo(3));
    }

    #endregion CreateDbCommand

    #region Commands of another provider

    [Test]
    public async Task ExecuteDbCommandAsyncThrowsArgumentExceptionWhenCommandIsNotSqlCommand()
    {
        NpgsqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandAsync(wrongCommand));
    }

    [Test]
    public async Task ExecuteDbCommandNoReturnAsyncThrowsArgumentExceptionWhenCommandIsNotSqlCommand()
    {
        NpgsqlCommand wrongCommand = new("SELECT 1");

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.ExecuteDbCommandNoReturnAsync(wrongCommand));
    }

    #endregion Commands of another provider

    #region Bulk insert

    [Test]
    public async Task BulkInsertAsyncThrowsArgumentExceptionWhenTheOptionsAreOfAnotherProvider()
    {
        using DataTable data = new();

        await Assert.ThrowsAsync<ArgumentException>(async () => await _service.BulkInsertAsync(data, "dbo.Items", bulkInsertOptions: new PostgresBulkInsertOptions()));
    }

    #endregion Bulk insert
}
