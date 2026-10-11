using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Samples.Interfaces.Services;
using Bacon.DBConnectionHandler.Samples.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Security.Cryptography;

namespace Bacon.DBConnectionHandler.Samples.Controllers;

[ApiController]
[Route("api/connections")]
public class WeatherForecastController(IDefaultConnectionStringResolver defaultConnectionStringResolver, /*[FromKeyedServices("")]*/IConnectionHandlerService mssqlConnectionHandlerService, [FromKeyedServices("postgres")] IConnectionHandlerService postgresConnectionHandlerService
    /*, [FromKeyedServices("mysql")] IConnectionHandlerService mysqlConnectionHandlerService*/, [FromKeyedServices("sqlite")] IConnectionHandlerService sqliteConnectionHandlerService, IConnectionHandlerFactory connectionHandlerFactory
    , IConnectionHandlerManager connectionHandlerManager) : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    #region Tests

    [HttpPost]
    [Route("test/mssql")]
    public async Task<IEnumerable<WeatherForecast>> TestMssql(CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = new()
        {
            { "TotoID", 1 }
        };

        await mssqlConnectionHandlerService.ExecuteNoReturnAsync("dbo.GetTestTable", parameters, cancellationToken: cancellationToken);

        IEnumerable<DataTable> dataTables = await mssqlConnectionHandlerService.ExecuteAsync("dbo.GetTestTable", parameters, cancellationToken: cancellationToken);

        dataTables = await mssqlConnectionHandlerService.ExecuteDbCommandAsync(mssqlConnectionHandlerService.CreateDbCommand("dbo.GetTestTable", CommandType.StoredProcedure, parameters: parameters), cancellationToken: cancellationToken);

        dataTables = await mssqlConnectionHandlerService.ExecuteDbCommandAsync(mssqlConnectionHandlerService.CreateDbCommand("select TotoID, Tata from dbo.TestTable where TotoID = @TotoID", CommandType.Text, parameters: parameters), cancellationToken: cancellationToken);

        return [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })];
    }

    [HttpPost]
    [Route("test/postgres")]
    public async Task<IEnumerable<WeatherForecast>> TestPostgres(CancellationToken cancellationToken)
    {
        int id = RandomNumberGenerator.GetInt32(int.MaxValue);

        Dictionary<string, object?> parameters = new()
        {
            { "p_toto_id", id }
        };

        Dictionary<string, object?> procParameters = new()
        {
            { "p_toto_id", id },
            { "p_tata", "test" }
        };

        //calling the procedure to insert data and return nothing
        await postgresConnectionHandlerService.ExecuteNoReturnAsync("public.proc_get_test_table", procParameters, cancellationToken: cancellationToken);

        IEnumerable<DataTable> dataTables = await postgresConnectionHandlerService.ExecuteAsync("get_test_table", parameters, cancellationToken: cancellationToken);

        dataTables = await postgresConnectionHandlerService.ExecuteDbCommandAsync(postgresConnectionHandlerService.CreateDbCommand("select \"TotoID\", \"Tata\" from \"TestTable\" where \"TotoID\" = @p_toto_id", CommandType.Text, parameters: parameters), cancellationToken: cancellationToken);

        return [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })];
    }

    [HttpPost]
    [Route("test/mysql")]
    public async Task<IEnumerable<WeatherForecast>> TestMySql(CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = new()
        {
            { "TotoID", 1 }
        };

        await mssqlConnectionHandlerService.ExecuteNoReturnAsync("GetTestTable", parameters, cancellationToken: cancellationToken);

        IEnumerable<DataTable> dataTables = await mssqlConnectionHandlerService.ExecuteAsync("GetTestTable", parameters, cancellationToken: cancellationToken);

        dataTables = await mssqlConnectionHandlerService.ExecuteDbCommandAsync(mssqlConnectionHandlerService.CreateDbCommand("GetTestTable", CommandType.StoredProcedure, parameters: parameters), cancellationToken: cancellationToken);

        dataTables = await mssqlConnectionHandlerService.ExecuteDbCommandAsync(mssqlConnectionHandlerService.CreateDbCommand("select TotoID, Tata from TestTable where TotoID = @TotoID", CommandType.Text, parameters: parameters), cancellationToken: cancellationToken);

        return [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })];
    }

    [HttpPost]
    [Route("test/sqlite")]
    public async Task<IEnumerable<WeatherForecast>> TestSqlite(CancellationToken cancellationToken)
    {
        await sqliteConnectionHandlerService.ExecuteNoReturnAsync("CREATE TABLE IF NOT EXISTS TestTable (TotoID INTEGER PRIMARY KEY, Tata TEXT NOT NULL); insert into TestTable (TotoID, Tata) select 1, 'test' where not exists (select 1 from TestTable);", cancellationToken: cancellationToken);

        int id = RandomNumberGenerator.GetInt32(int.MaxValue);

        Dictionary<string, object?> parameters = new()
        {
            { "TotoID", id }
        };

        await sqliteConnectionHandlerService.ExecuteNoReturnAsync("insert into TestTable (TotoID, Tata) values (@TotoID, 'test')", parameters, cancellationToken: cancellationToken);

        IEnumerable<DataTable> dataTables = await sqliteConnectionHandlerService.ExecuteAsync("SELECT TotoID, Tata FROM TestTable WHERE TotoID = @TotoID;", parameters, cancellationToken: cancellationToken);

        dataTables = await sqliteConnectionHandlerService.ExecuteDbCommandAsync(sqliteConnectionHandlerService.CreateDbCommand("select TotoID, Tata from TestTable", CommandType.Text, parameters: parameters), cancellationToken: cancellationToken);

        return [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })];
    }

    [HttpPost]
    [Route("test/factory/{name}")]
    public async Task<IEnumerable<WeatherForecast>> TestFactory([FromRoute] string name, CancellationToken cancellationToken)
    {
        if (name == "default")
        {
            name = string.Empty;
        }

        IConnectionHandlerService connectionHandlerService = await connectionHandlerFactory.GetHandlerAsync(name, cancellationToken);

        IEnumerable<DataTable> dataTables = await connectionHandlerService.ExecuteDbCommandAsync(connectionHandlerService.CreateDbCommand("select 1", CommandType.Text), cancellationToken: cancellationToken);

        return [.. Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })];
    }

    #endregion Tests

    #region Connection Manager

    [HttpGet]
    [Route("dynamic/names")]
    public async Task<IReadOnlyCollection<string>> GetDynamicConnectionNames(CancellationToken cancellationToken)
    {
        return await connectionHandlerManager.GetNamesAsync(cancellationToken);
    }

    [HttpPost]
    [Route("dynamic/{name}/providers/{provider}")]
    public async Task<ConnectionChangeResult> CreateDynamicConnectionHandle([FromRoute] string name, [FromRoute] string provider, CancellationToken cancellationToken)
    {
        if (name == "default")
        {
            name = string.Empty;
        }

        ConnectionHandlerOptions? connectionHandlerOptions = defaultConnectionStringResolver.GetOption(provider) ?? throw new ArgumentException("Invalid provider");

        return await connectionHandlerManager.AddAsync(new ConnectionRecord(name, provider, connectionHandlerOptions), cancellationToken);
    }

    [HttpPut]
    [Route("dynamic/{name}/providers/{provider}")]
    public async Task<ConnectionChangeResult> UpdateDynamicConnectionHandle([FromRoute] string name, [FromRoute] string provider, CancellationToken cancellationToken)
    {
        if (name == "default")
        {
            name = string.Empty;
        }

        ConnectionHandlerOptions? connectionHandlerOptions = defaultConnectionStringResolver.GetOption(provider) ?? throw new ArgumentException("Invalid provider");

        return await connectionHandlerManager.UpdateAsync(new ConnectionRecord(name, provider, connectionHandlerOptions), cancellationToken);
    }

    [HttpDelete]
    [Route("dynamic/{name}")]
    public async Task<ConnectionChangeResult> DeleteDynamicConnectionHandle([FromRoute] string name, CancellationToken cancellationToken)
    {
        if (name == "default")
        {
            name = string.Empty;
        }

        return await connectionHandlerManager.RemoveAsync(name, cancellationToken);
    }

    #endregion Connection Manager
}
