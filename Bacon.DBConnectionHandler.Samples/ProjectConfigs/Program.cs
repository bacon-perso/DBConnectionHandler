using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Mssql.Extensions;
using Bacon.DBConnectionHandler.Mysql.Extensions;
using Bacon.DBConnectionHandler.Postgres.Extensions;
using Bacon.DBConnectionHandler.Samples.Interfaces.Services;
using Bacon.DBConnectionHandler.Samples.Models;
using Bacon.DBConnectionHandler.Samples.Services;
using Bacon.DBConnectionHandler.Samples.Services.Stores;
using Bacon.DBConnectionHandler.Sqlite.Extensions;
using Microsoft.Data.Sqlite;
using StackExchange.Redis;
using System.Globalization;


WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(args);

// Add services to the container.

webApplicationBuilder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
webApplicationBuilder.Services.AddOpenApi();

#region Configs

//normaly this part should directly read the connection string instead of constructing it

string mssqlServer = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MSSQL:DatabaseServer"]!;
string mssqlUsername = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MSSQL:DatabaseUsername"]!;
string mssqlPassword = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MSSQL:DatabasePassword"]!;
string mssqlDatabase = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MSSQL:DatabaseName"]!;

string postgresServer = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["PostGres:DatabaseServer"]!;
string postgresUsername = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["PostGres:DatabaseUsername"]!;
string postgresPassword = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["PostGres:DatabasePassword"]!;
string postgresDatabase = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["PostGres:DatabaseName"]!;

string mySqlServer = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MySql:DatabaseServer"]!;
string mySqlPort = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MySql:DatabasePort"]!;
string mySqlUsername = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MySql:DatabaseUsername"]!;
string mySqlPassword = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MySql:DatabasePassword"]!;
string mySqlDatabase = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["MySql:DatabaseName"]!;

string sqliteDatabasePath = webApplicationBuilder.Configuration.GetSection("DatabaseSettings")["Sqlite:DatabasePath"]!;

string redisServer = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Server"]!;
ushort redisPort = Convert.ToUInt16(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Port"]!, CultureInfo.InvariantCulture);
string redisPassword = webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:Password"]!;
bool redisUseSsl = Convert.ToBoolean(webApplicationBuilder.Configuration.GetSection("CacheSettings")["RedisSettings:UseSsl"]!);

DefaultConnectionStrings defaultConnectionStrings = new(
    $"Server={mssqlServer};Database={mssqlDatabase};User Id={mssqlUsername};password={mssqlPassword};Trusted_Connection=False;MultipleActiveResultSets=true;TrustServerCertificate=True",
    $"Server={mySqlServer};Port={mySqlPort};Database={mySqlDatabase};Uid={mySqlUsername};Pwd={mySqlPassword};SslMode=None;AllowPublicKeyRetrieval=True;",
    $"Host={postgresServer};Port=5432;Database={postgresDatabase};Username={postgresUsername};Password={postgresPassword};Trust Server Certificate=true",
    new SqliteConnectionStringBuilder()
    {
        DataSource = sqliteDatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true
    }.ToString());

webApplicationBuilder.Services.AddSingleton(defaultConnectionStrings);
webApplicationBuilder.Services.AddTransient<IDefaultConnectionStringResolver, DefaultConnectionStringResolver>();

#endregion Configs

//opt-in commands that allows packet multiplexing for a preview feature
/*
Sources:
- AppContext switches in SqlClient — Microsoft Learn (https://learn.microsoft.com/en-us/sql/connect/ado-net/appcontext-switches?view=sql-server-ver17)
- SqlClient 7.0 release notes (https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md)
- Get a 180% speed increase on large async reads — ErikEJ's blog (https://erikej.github.io/sqlclient/dotnet/performance/2025/10/20/sqlclient-performance.html)
*/
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityAsyncBehaviour", false);
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityProcessSni", false);

#region Redis Cache

ConfigurationOptions configurationOptions = new()
{
    AbortOnConnectFail = false,
    AllowAdmin = true,
    ConnectTimeout = 5000,
    ConnectRetry = 5,
    KeepAlive = 60,
    EndPoints = {
                    {
                        redisServer,
                        redisPort
                    }
                },
    ReconnectRetryPolicy = new LinearRetry(5000),
    Password = redisPassword,
    Ssl = redisUseSsl
};

//creating the connection multiplexer
ConnectionMultiplexer connectionMultiplexer = ConnectionMultiplexer.Connect(configurationOptions);
webApplicationBuilder.Services.AddSingleton(connectionMultiplexer);

//creating the redis database
IDatabase database = connectionMultiplexer.GetDatabase();
webApplicationBuilder.Services.AddSingleton(database);

#endregion

webApplicationBuilder.Services.AddConnectionHandler()
    .UseStore<CustomStore>()
    .AddMssql(o =>
    {
        o.ConnectionString = defaultConnectionStrings.MssqlConnectionString;
    })
    .AddPostgres("postgres", o =>
    {
        o.ConnectionString = defaultConnectionStrings.PostgresConnectionString;
    })
    .AddMysqlProvider()
    //.ConfigureDynamicConnections(o =>
    //{
    //    // How long a node keeps a loaded connection before re-reading the store. null = never (default)
    //    o.CacheDuration = TimeSpan.FromMinutes(5);

    //    // How long a replaced or removed connection keeps working before it is disposed. Default: 30 seconds
    //    o.RetireDelay = TimeSpan.FromSeconds(30);
    //})
    .AddMysql("mysql", o =>
    {
        o.ConnectionString = defaultConnectionStrings.MysqlConnectionString;
    })
    .AddSqlite("sqlite", o =>
    {
        o.ConnectionString = defaultConnectionStrings.SqliteConnectionString;
    })
    .Build();

WebApplication webApplication = webApplicationBuilder.Build();

webApplication.Lifetime.ApplicationStopped.Register(() =>
{
    SqliteConnection.ClearAllPools();   // release the pooled file handles first

    if (File.Exists(sqliteDatabasePath))
    {
        File.Delete(sqliteDatabasePath);
    }
});

// Configure the HTTP request pipeline.
if (webApplication.Environment.IsDevelopment())
{
    webApplication.MapOpenApi();
}

webApplication.UseHttpsRedirection();

webApplication.UseAuthorization();

webApplication.MapControllers();

webApplication.Run();
