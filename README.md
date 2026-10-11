# Bacon.DBConnectionHandler

A small .NET 10 library that gives an application a single, provider-agnostic way to run commands against SQL Server, PostgreSQL, SQLite and MySQL. It handles connection lifetime for you (a pooled connection is opened per call and always released), optionally adds health checks, and supports connections defined at startup (**static**) or at runtime (**dynamic**, e.g. one per tenant).

- [Packages](#packages)
- [Quick start](#quick-start)
- [Static connections](#static-connections)
- [Using a connection](#using-a-connection)
- [Dynamic connections](#dynamic-connections)
- [Custom store and encryption in production](#custom-store-and-encryption-in-production)
- [Health checks](#health-checks)
- [Provider notes](#provider-notes)
- [SQL Server: enable the async reader](#sql-server-enable-the-async-reader)
- [Behavior to know about](#behavior-to-know-about)
- [Sample app](#sample-app)
- [Building and testing](#building-and-testing)

## Packages

The core has no database driver. Reference the core and the provider(s) you need.

| Project | Database | Driver |
|---|---|---|
| `Bacon.DBConnectionHandler` | Core contracts, builder, dynamic connection manager | `Microsoft.Extensions.*` abstractions only |
| `Bacon.DBConnectionHandler.Mssql` | SQL Server | `Microsoft.Data.SqlClient` |
| `Bacon.DBConnectionHandler.Postgres` | PostgreSQL | `Npgsql` |
| `Bacon.DBConnectionHandler.Sqlite` | SQLite | `Microsoft.Data.Sqlite` |
| `Bacon.DBConnectionHandler.Mysql` | MySQL | `MySqlConnector` |

Target framework: `net10.0`.

## Quick start

```csharp
using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Mssql.Extensions;
using Bacon.DBConnectionHandler.Postgres.Extensions;
using Bacon.DBConnectionHandler.Sqlite.Extensions;

webApplicationBuilder.Services.AddConnectionHandler()
    .AddMssql(o =>
    {
        o.ConnectionString = mssqlConnectionString;          // default connection (empty name)
    })
    .AddPostgres("postgres", o =>
    {
        o.ConnectionString = postgresConnectionString;       // named connection
    })
    .AddSqlite("sqlite", o =>
    {
        o.ConnectionString = sqliteConnectionString;
    })
    .Build();                                                // must always be called
```

`Build()` opens every static connection once, so **the application fails to start if a database is unreachable**. If any connection fails, an `AggregateException` is thrown (one `InvalidOperationException` per failed connection) and nothing is registered.

`Build()` must be called even for an app that only uses dynamic connections. Forgetting it registers nothing, and the factory tells you so.

## Static connections

Static connections are defined in code at startup and live for the whole process. Each one is registered as a keyed singleton `IConnectionHandlerService`, where the key is the connection name (`""` is the default connection).

| Registered with | Injected as |
|---|---|
| `AddMssql(o => ...)` (no name) | `IConnectionHandlerService` |
| `AddPostgres("postgres", o => ...)` | `[FromKeyedServices("postgres")] IConnectionHandlerService` |

```csharp
[ApiController]
[Route("api/connections")]
public class WeatherForecastController(
    IConnectionHandlerService mssqlConnectionHandlerService,                                      // the default connection
    [FromKeyedServices("postgres")] IConnectionHandlerService postgresConnectionHandlerService,   // a named connection
    [FromKeyedServices("sqlite")] IConnectionHandlerService sqliteConnectionHandlerService) : ControllerBase
{
}
```

Duplicate names, or a second default connection, throw as soon as they are added.

## Using a connection

`IConnectionHandlerService` is the same on every provider. Results come back as one `DataTable` per result set.

```csharp
// Call a routine by name with a parameter dictionary, and read the result sets
Dictionary<string, object?> parameters = new()
{
    { "TotoID", 1 }
};

IEnumerable<DataTable> dataTables = await mssqlConnectionHandlerService.ExecuteAsync("dbo.GetTestTable", parameters, cancellationToken: cancellationToken);

// Call a routine and ignore the result
await mssqlConnectionHandlerService.ExecuteNoReturnAsync("dbo.GetTestTable", parameters, cancellationToken: cancellationToken);

// Run your own provider command (a text query, or anything the routine helpers don't cover)
DbCommand dbCommand = mssqlConnectionHandlerService.CreateDbCommand(
    "select TotoID, Tata from dbo.TestTable where TotoID = @TotoID",
    CommandType.Text,
    parameters: parameters);

IEnumerable<DataTable> rows = await mssqlConnectionHandlerService.ExecuteDbCommandAsync(dbCommand, cancellationToken: cancellationToken);
await mssqlConnectionHandlerService.ExecuteDbCommandNoReturnAsync(dbCommand, cancellationToken: cancellationToken);
```

| Member | Purpose |
|---|---|
| `ExecuteAsync` / `ExecuteNoReturnAsync` | Call a routine by name with a parameter dictionary |
| `CreateDbCommand` | Build a provider command (`SqlCommand`, `NpgsqlCommand`, ...) to customize |
| `ExecuteDbCommandAsync` / `ExecuteDbCommandNoReturnAsync` | Run a command built with `CreateDbCommand` |
| `BulkInsertAsync` | Insert a whole `DataTable` using the database's native bulk path |

Things to know:

- `ExecuteDbCommand*` take ownership of the command and dispose it. A command built by another provider throws `ArgumentException`.
- You can pass your own `DbCommand`, as long as it is the provider's own command type.
- A connection is opened per call and released when the call ends. Never keep one.

### Bulk insert

```csharp
DataTable data = new();
data.Columns.Add("id", typeof(int));
data.Columns.Add("name", typeof(string));
data.Rows.Add(1, "first");

await postgresConnectionHandlerService.BulkInsertAsync(
    data,
    "public.items",
    columnsMapping: new Dictionary<string, string> { { "name", "item_name" } },   // optional: source column -> destination column
    bulkInsertTimeout: 30,
    bulkInsertOptions: new PostgresBulkInsertOptions(),                           // optional, provider specific
    cancellationToken: cancellationToken);
```

Each provider has its own bulk options type. Passing another provider's options throws `ArgumentException`.

| Provider | Mechanism | Options |
|---|---|---|
| Mssql | `SqlBulkCopy` | `MssqlBulkInsertOptions.BulkCopyOptions` |
| Postgres | Binary `COPY` | `PostgresBulkInsertOptions.ColumnTypes` overrides inferred types |
| Sqlite | One transaction with a reused `INSERT` | none |
| Mysql | `MySqlBulkCopy` | none |

## Dynamic connections

Dynamic connections are added, updated and removed at runtime, typically one per tenant. They are kept in an `IConnectionStore` and cached per node.

The providers must be known at startup, because they are resolved from the container. List every database engine the app may be asked to create dynamic connections for:

```csharp
webApplicationBuilder.Services.AddConnectionHandler()
    .AddPostgresProvider()        // support only, no connection
    .AddMysqlProvider()
    .Build();
```

(`AddMssql`, `AddPostgres`, ... also register their provider, so a provider you already use statically does not need it again.)

### Managing connections

Inject `IConnectionHandlerManager`. Every change returns a `ConnectionChangeResult` instead of throwing:

```csharp
ConnectionChangeResult result = await connectionHandlerManager.AddAsync(
    new ConnectionRecord("tenant-a", PostgresProviderDefaults.Provider, new PostgresConnectionHandlerOptions { ConnectionString = "..." }),
    cancellationToken);

if (!result.Succeeded)
{
    // result.Status tells why, result.Error carries the exception when there is one
}

await connectionHandlerManager.UpdateAsync(new ConnectionRecord("tenant-a", PostgresProviderDefaults.Provider, newOptions), cancellationToken);
await connectionHandlerManager.RemoveAsync("tenant-a", cancellationToken);
IReadOnlyCollection<string> names = await connectionHandlerManager.GetNamesAsync(cancellationToken);
```

| Status | Meaning |
|---|---|
| `Added`, `Updated`, `Removed` | Success (`Succeeded` is true) |
| `NotFound` | Update or remove of an unknown name |
| `AlreadyExists` | The name is used by a static connection or already stored |
| `NotAllowed` | Update or remove of a static connection, or use of the reserved empty name |
| `Invalid` | Wrong options type for the provider, or `Validate()` failed |
| `UnknownProvider` | No provider with that id is registered |
| `TestFailed` | The database could not be opened. Nothing is changed |

`AddAsync` and `UpdateAsync` open the database first. A failure of the store itself is thrown, not returned.

### Resolving a dynamic connection

Dynamic connections cannot be injected. Resolve them through the factory on every use:

```csharp
IConnectionHandlerService connectionHandlerService = await connectionHandlerFactory.GetHandlerAsync("tenant-a", cancellationToken);

IEnumerable<DataTable> dataTables = await connectionHandlerService.ExecuteDbCommandAsync(
    connectionHandlerService.CreateDbCommand("select 1", CommandType.Text),
    cancellationToken: cancellationToken);
```

`GetHandlerAsync` resolves static connections first, then dynamic ones (loading from the store on a miss). The empty name is the static default. Do not keep the returned handler: after an update or removal the old one is retired.

### Options

```csharp
.ConfigureDynamicConnections(o =>
{
    // How long a node keeps a loaded connection before re-reading the store. null = never (default)
    o.CacheDuration = TimeSpan.FromMinutes(5);

    // How long a replaced or removed connection keeps working before it is disposed. Default: 30 seconds
    o.RetireDelay = TimeSpan.FromSeconds(30);
})
```

- A static connection is read-only to the manager.
- Concurrent first calls for the same name share one load. A failed load or an unknown name is never cached.
- After every change the manager drops the local cache entry. **`CacheDuration` is the only mechanism that bounds staleness on the other nodes**: there is deliberately no messaging layer. If several nodes share a store, set it.

### The default store

The default `IConnectionStore` is in memory: changes last until the app restarts, and are not shared between nodes. For anything beyond a single instance, plug in your own store.

## Custom store and encryption in production

Implement `IConnectionStore` (or `IListableConnectionStore` if the backing system can list its keys) and register it with `UseStore<TStore>()`:

```csharp
webApplicationBuilder.Services.AddConnectionHandler()
    .UseStore<CustomStore>()
    .AddMssql(o => { o.ConnectionString = mssqlConnectionString; })
    .Build();
```

```csharp
public interface IConnectionStore
{
    ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default);
    ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default);
    ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default);
}

// IListableConnectionStore adds: ValueTask<IReadOnlyCollection<string>> GetNamesAsync(CancellationToken cancellationToken = default)
```

Only dynamic connections go through the store, so the secrets of static connections never reach it.

> **Encrypt connection strings in your store. This is required in production.**
>
> A `ConnectionRecord` holds the connection string, which contains database credentials. The library does not encrypt anything, and the store is the only place that persists it, so **the store you write is responsible for encrypting what it saves and decrypting what it loads**.
>
> - The `CustomStore` in the sample app writes the record as plain JSON into Redis (with a 60 second expiry, to keep the demo clean). It carries a `real store would encrypt here` comment where encryption belongs. **Do not copy it to production as is.**
> - `ConnectionRecord.ToString()` hides the connection string so it stays out of logs. This protects logs only: a serializer, a destructuring logger or a debugger still sees `Options`.
> - Treat `Options` as immutable once handed over: the in-memory store keeps the reference.

### What to do

- **Encrypt at rest** with a key you manage. In ASP.NET Core, `IDataProtector` (ASP.NET Core Data Protection, with its key ring persisted somewhere shared if you run several nodes) is the easiest. Cloud key services (Azure Key Vault, AWS KMS, GCP KMS) work too, and are the right choice when keys must be rotated or audited.
- **Share the keys across nodes.** Every node reading the store must be able to decrypt what any other node wrote.
- **Plan for key rotation.** Use a keying system that can still read data written with an older key.
- **Secure the transport and the backend.** Use TLS to the backend (Redis `Ssl = true`, `sslmode=require`, ...), restrict network access, and give the credentials of the store only the access it needs.
- **Don't log what you decrypt.**

### Example: encrypting with Data Protection

The store has to deserialize the options into the right type. `IConnectionProvider.OptionsType` gives it, and the sample resolves it with a small `IDefaultConnectionStringResolver.GetOptionType(provider)` helper. A store backed by a database or cache can do the same. This sketch encrypts the serialized options with `IDataProtector`:

```csharp
public class EncryptedStore(IDatabase database, IDefaultConnectionStringResolver defaultConnectionStringResolver, IDataProtectionProvider dataProtectionProvider) : IConnectionStore
{
    private static readonly string _keyPrefix = "connectionhandler:dynamic:";

    // Pick a purpose string and keep it stable: changing it makes existing data unreadable
    private readonly IDataProtector _dataProtector = dataProtectionProvider.CreateProtector("Bacon.DBConnectionHandler.ConnectionStore");

    public async ValueTask<ConnectionRecord?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        RedisValue redisValue = await database.StringGetAsync(_keyPrefix + name);

        if (!redisValue.HasValue)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(redisValue.ToString());
        JsonElement root = document.RootElement;

        string provider = root.GetProperty(nameof(ConnectionRecord.Provider)).GetString()!;
        Type optionsType = defaultConnectionStringResolver.GetOptionType(provider) ?? throw new InvalidOperationException($"DB - No provider '{provider}' is registered for the stored connection '{name}'");

        // Decrypt only the options: the name and provider are not secret
        string optionsJson = _dataProtector.Unprotect(root.GetProperty(nameof(ConnectionRecord.Options)).GetString()!);
        ConnectionHandlerOptions options = (ConnectionHandlerOptions)JsonSerializer.Deserialize(optionsJson, optionsType)!;

        return new ConnectionRecord(name, provider, options);
    }

    public async ValueTask SetAsync(ConnectionRecord connectionRecord, CancellationToken cancellationToken = default)
    {
        string encryptedOptions = _dataProtector.Protect(JsonSerializer.Serialize(connectionRecord.Options, connectionRecord.Options.GetType()));

        string json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { nameof(ConnectionRecord.Provider), connectionRecord.Provider },
            { nameof(ConnectionRecord.Options), encryptedOptions }
        });

        await database.StringSetAsync(_keyPrefix + connectionRecord.Name, json);
    }

    public async ValueTask<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        return await database.KeyDeleteAsync(_keyPrefix + name);
    }
}
```

Remember to call `services.AddDataProtection()` (and configure where its key ring is stored) so `IDataProtectionProvider` is available.

Also:

- Serialize the options with their **runtime type** (`connectionRecord.Options.GetType()`), as above. Serializing as the abstract `ConnectionHandlerOptions` fails when reading back ("Deserialization of interface or abstract types is not supported").
- If your store cannot enumerate its keys, implement `IConnectionStore` only. `manager.GetNamesAsync()` then returns the names loaded on this node.

## Health checks

Pass a `ConnectionHandlerHealthCheckOptions` to a **static** connection to register an ASP.NET Core health check for it. A null object (the default) registers none. Dynamic connections have no health check.

```csharp
.AddPostgres("postgres", o =>
{
    o.ConnectionString = postgresConnectionString;
    o.ConnectionHandlerHealthCheckOptions = new ConnectionHandlerHealthCheckOptions
    {
        FailureStatus = HealthStatus.Degraded,       // default: Unhealthy
        Timeout = TimeSpan.FromSeconds(5),
        Tags = ["database", "postgres"]              // default: ["database"]
    };
})
```

| Property | Default |
|---|---|
| `Name` | The provider's default name (e.g. `sqlserver`), suffixed `:<connection name>` for named connections. An explicit `Name` always wins |
| `HealthQuery` | `SELECT 1;` |
| `FailureStatus` | `Unhealthy` |
| `Tags` | `["database"]` |
| `Timeout` | The health check's own default |

You still map the endpoint yourself (`app.MapHealthChecks("/health")`).

## Provider notes

- **Mssql**: `ExecuteAsync` runs the routine with `CommandType.StoredProcedure`. See [the async reader](#sql-server-enable-the-async-reader) below.
- **Postgres**: no `CommandType.StoredProcedure`.
  - `ExecuteAsync` builds `SELECT * FROM routine(a => @a, ...)` for **functions** that return data, and `ExecuteNoReturnAsync` builds `CALL routine(...)` for **procedures**.
  - Parameters are passed by name, so the dictionary keys must match the argument names.
  - Void functions and `INOUT` procedures go through `CreateDbCommand` + `ExecuteDbCommand*`.
  - Routine, table, column and parameter names are validated against the Postgres identifier rules before they are put in the SQL. Quoted identifiers are accepted for routine, table and column names.
- **Sqlite**: SQLite has no stored routines, so `ExecuteAsync` / `ExecuteNoReturnAsync` run the "routine name" as plain SQL text with the parameters bound by name.
  - `CreateDbCommand` only accepts `CommandType.Text` (anything else throws `ArgumentException`).
  - A connection is opened per call, so a plain `:memory:` database is empty on every call. Use a file or a shared memory database.
  - The default open mode creates a missing file, so the startup test does not catch a wrong path unless the connection string uses `Mode=ReadWrite`.
- **Mysql**: `ExecuteAsync` / `ExecuteNoReturnAsync` run the routine with `CommandType.StoredProcedure`, and parameter names are prefixed with `@`. Bulk insert uses `LOAD DATA LOCAL INFILE`: the connection string must allow local infile and the server must enable `local_infile`.

On every provider, a new connection is opened per call and the driver's own pool (keyed by connection string) does the pooling, so nothing is disposed when a dynamic connection is retired.

## SQL Server: enable the async reader

By default, reads on the Mssql provider use the **legacy fallback**: a synchronous reader that **blocks the calling thread** for the whole query and only honors the cancellation token before the command starts.

The async reader (with packet multiplexing) is opt-in in `Microsoft.Data.SqlClient`. It is **strongly recommended for throughput under load**. Turn it on at startup, **before any connection is opened**, by setting both `AppContext` switches to `false`:

```csharp
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityAsyncBehaviour", false);
AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseCompatibilityProcessSni", false);
```

The library selects the async reader only when **both** switches are `false`. If either is left at its default, the synchronous reader is used. The sample sets both.

Sources:

- [AppContext switches in SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/appcontext-switches?view=sql-server-ver17) (Microsoft Learn)
- [SqlClient 7.0 release notes](https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md)
- [Get a 180% speed increase on large async reads](https://erikej.github.io/sqlclient/dotnet/performance/2025/10/20/sqlclient-performance.html) (ErikEJ)

## Behavior to know about

- **Duplicate column names are rejected.** A result set that contains the same column name twice throws `InvalidOperationException` (the `DuplicateNameException` is the inner exception), on every provider and read path. Columns are never silently renamed: alias them in the query. The query has already run on the server when this is thrown.
- **Services are stateless.** Provider services are singletons (static) or cached per node (dynamic), so they keep only their constructor inputs and open a new connection per call.
- **Log and rethrow.** Providers log failures (messages prefixed `"DB - "`) and rethrow; they never swallow exceptions. The manager is the exception: database failures when adding or updating a dynamic connection come back in the `ConnectionChangeResult`.
- **No `ConfigureAwait` surprises.** Library code calls `.ConfigureAwait(false)` on every `await`, so it never resumes on a caller's UI or request context.
- **Provider ids are persisted.** `<P>ProviderDefaults.Provider` (e.g. `PostgresProviderDefaults.Provider`) is stored inside saved `ConnectionRecord`s and never changes.

## Sample app

`Bacon.DBConnectionHandler.Samples` is an ASP.NET Core Web API showing static Mssql, Postgres and Sqlite connections, the Mysql provider, a Redis-backed `CustomStore`, and endpoints that exercise the factory and the manager.

It reads its settings from `appsettings.Development.json` (or user secrets) under `DatabaseSettings`:

| Section | Keys |
|---|---|
| `MSSQL` | `DatabaseServer`, `DatabaseUsername`, `DatabasePassword`, `DatabaseName` |
| `PostGres` | `DatabaseServer`, `DatabaseUsername`, `DatabasePassword`, `DatabaseName` |
| `MySql` | `DatabaseServer`, `DatabasePort`, `DatabaseUsername`, `DatabasePassword`, `DatabaseName` |
| `Sqlite` | `DatabasePath` |

and a `CacheSettings:RedisSettings` section (`Server`, `Port`, `Password`, `UseSsl`) for the store.

```bash
dotnet run --project Bacon.DBConnectionHandler.Samples/Bacon.DBConnectionHandler.Samples.csproj
```

| Endpoint | Does |
|---|---|
| `POST api/connections/test/mssql` / `postgres` / `sqlite` | Runs the sample calls on the static connections |
| `POST api/connections/test/factory/{name}` | Resolves a connection through the factory (`default` for the empty name) |
| `GET api/connections/dynamic/names` | Lists the stored dynamic connection names |
| `POST` / `PUT api/connections/dynamic/{name}/providers/{provider}` | Adds or updates a dynamic connection |
| `DELETE api/connections/dynamic/{name}` | Removes a dynamic connection |

## Building and testing

```bash
dotnet build Bacon.DBConnectionHandler.slnx
dotnet test Bacon.DBConnectionHandler.Tests/Bacon.DBConnectionHandler.Tests.csproj
```

The test suite needs no database server: it runs offline. Library projects treat warnings as errors in Release (code analysis rules and missing XML docs included), so build with `-c Release` before considering a change done.
