# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build everything
dotnet build Bacon.DBConnectionHandler.slnx

# Build one project (core library or a provider)
dotnet build Bacon.DBConnectionHandler/Bacon.DBConnectionHandler.csproj
dotnet build Bacon.DBConnectionHandler.Mssql/Bacon.DBConnectionHandler.Mssql.csproj

# Run the sample app
dotnet run --project Bacon.DBConnectionHandler.Samples/Bacon.DBConnectionHandler.Samples.csproj
```

Every library project (core and providers) treats warnings as errors in Release (this includes code analysis rules such as CA1001), so also build with `-c Release` before considering a change done.

## Solution layout

.NET 10. A provider-agnostic core plus one NuGet-style project per database; consumers reference the core and the provider(s) they need.

| Project | Role |
|---|---|
| `Bacon.DBConnectionHandler` | Core: public contracts, builder, registration, dynamic connection manager, shared base classes. References only the `Microsoft.Extensions.*` abstractions (DI, Logging, HealthChecks), no database driver. |
| `Bacon.DBConnectionHandler.Mssql` | SQL Server (`Microsoft.Data.SqlClient`, `AspNetCore.HealthChecks.SqlServer`). The reference implementation. |
| `Bacon.DBConnectionHandler.Postgres` | PostgreSQL (`Npgsql`, `AspNetCore.HealthChecks.NpgSql`). |
| `Bacon.DBConnectionHandler.Sqlite` | SQLite (`Microsoft.Data.Sqlite`, `AspNetCore.HealthChecks.Sqlite`). The full `Microsoft.Data.Sqlite` package (not `.Core`) is used for two reasons: `AspNetCore.HealthChecks.Sqlite` depends on it, so the native `SQLitePCLRaw` bundle comes with it anyway, and referencing it directly at 10.0.12 overrides the 8.0.0 the health check package pulls in, whose native library has a known high severity vulnerability. Keep this reference up to date and check with `dotnet list package --vulnerable --include-transitive`. |
| `Bacon.DBConnectionHandler.Mysql` | MySQL (`MySqlConnector`, `AspNetCore.HealthChecks.MySql`). Implemented like the other providers. |
| `Bacon.DBConnectionHandler.Samples` | ASP.NET Core Web API demonstrating usage (static Mssql, Postgres and Sqlite connections, plus `AddMysqlProvider()`). The Mysql static connection is commented out. |
| `Bacon.DBConnectionHandler.Tests` | NUnit 5 project covering the core (builder, factory, manager, dynamic registry, store, options) and the providers. See [Tests](#tests). |

The core and every provider expose internals to the Tests project (`InternalsVisibleTo`). Provider services and providers are `internal sealed`; only the options, bulk-insert options, `<P>ProviderDefaults` and the extension methods are public.

### Core folder layout

`Extensions/` (`AddConnectionHandler`, `Build`, `Add`, `AddProvider`, `UseStore`, `ConfigureDynamicConnections`), `Interfaces/Services/` (service, factory, manager, provider contracts), `Interfaces/Stores/` (store contracts), `Models/` (`ConnectionRecord`, `ConnectionHandle`, `ConnectionChangeResult`, `Enums.cs`), `Models/Options/`, `Registries/` (static and dynamic registries), `Services/` (builder, factory, manager, base classes), `Stores/` (default in-memory store).

## Architecture

There are two kinds of connections. They share the `IConnectionHandlerService` contract but are registered, resolved and managed differently.

| | Static | Dynamic |
|---|---|---|
| Defined | In code at startup (`Add<P>`), secrets typically from env/config | At runtime through `IConnectionHandlerManager` (e.g. tenants) |
| Lifecycle | Fixed for the process lifetime | Add, update, remove; materialized lazily, optional expiry |
| Startup test | `Build()` opens each one, fails fast | On `AddAsync`/`UpdateAsync` through the manager; lazily on first use when loaded from a store |
| Injection | `IConnectionHandlerService` (default) or `[FromKeyedServices("name")]` | Factory only: `GetHandlerAsync(name)` |
| Health check | Per connection, registered at build | None |
| Backing | Service collection (keyed singletons) | `IConnectionStore` (default: in-memory) + `DynamicConnectionRegistry` cache |
| Manager access | Read-only (`NotAllowed`) | Full |

### Static registration flow

```csharp
services.AddConnectionHandler()          // ConnectionHandlerExtensions -> returns a ConnectionHandlerBuilder
    .AddMssql(o => { o.ConnectionString = "..."; })             // static default connection (empty name)
    .AddPostgres("reporting", o => { ... })                     // static named connection
    .AddPostgresProvider()                                      // provider support only, no connection (for dynamic connections)
    .Build();                                                   // ConnectionHandlerBuilderExtensions.Build()
```

- `AddConnectionHandler()` registers the static `ConnectionHandlerRegistry`, the dynamic side (`DynamicConnectionOptions`, `TimeProvider`, `IConnectionStore` defaulting to `InMemoryConnectionHandlerStore`, `DynamicConnectionRegistry`, `IConnectionHandlerManager`) and `IConnectionHandlerFactory` (transient), and returns the builder. The dynamic side costs nothing until used.
- Each `Add<Provider>(name, configure)` (in the provider project) builds the options, calls `Validate()` (non-empty connection string), hands the builder a `ConnectionHandle` and also registers the provider (`Add<Provider>Provider()`, harmless if repeated). Nothing touches the service collection yet. Duplicate names, or a second default (empty-name) connection, throw immediately.
- `Add<Provider>Provider()` only registers the database engine support (an `IConnectionProvider`), with no connection. The providers must be known at startup because they are resolved from the container; the app lists every engine it may be asked to create dynamic connections for.
- `Build()` is what registers everything:
  1. Throws if no connection was added **and** no provider was registered (a dynamic-only app is valid).
  2. **Opens every static connection once at startup** via each handle's `Test` delegate, so the app fails to start if a database is unreachable. If any fail, an `AggregateException` is thrown and **nothing** is added to the service collection.
  3. Registers each connection as a **keyed singleton** `IConnectionHandlerService` (key = connection name, `""` = default) whose factory calls the handle's `Create(sp)`.
  4. Runs each handle's optional `ConfigureServices` (health checks, provider-specific registrations).
  5. If a default connection exists, also registers a plain (non-keyed) singleton `IConnectionHandlerService` that forwards to the keyed default, so consumers can just inject `IConnectionHandlerService`.
  6. Marks the static registry as built (`IsBuilt`). **`Build()` must be called even for a dynamic-only app**: the factory uses the flag to tell "Build() was never called" from "unknown name".
- Forgetting `.Build()` registers nothing; the factory detects an unbuilt registry and says so.

### Dynamic connections

```csharp
ConnectionChangeResult result = await manager.AddAsync(
    new ConnectionRecord("tenant-a", PostgresProviderDefaults.Provider, new PostgresConnectionHandlerOptions { ConnectionString = "..." }));

IConnectionHandlerService handler = await factory.GetHandlerAsync("tenant-a", cancellationToken);   // resolve on every use
```

- **`IConnectionHandlerManager`** (`ConnectionHandlerManager`, singleton): `AddAsync`, `UpdateAsync`, `RemoveAsync`, `GetNamesAsync`. There are no per-provider add/update/remove extension methods on purpose: callers build a `ConnectionRecord` and use `<P>ProviderDefaults.Provider` for the provider id.
- **Results, not exceptions**: every change returns a `ConnectionChangeResult(Name, Status, Error?)` (`Succeeded` is true for `Added`/`Updated`/`Removed`). Statuses: `Added`, `Updated`, `Removed`, `NotFound`, `AlreadyExists`, `NotAllowed`, `Invalid` (wrong options type or failed `Validate()`), `UnknownProvider` (no `IConnectionProvider` registered for the provider id), `TestFailed`. Failures of the store itself are thrown.
- **Rules**: a static connection is read-only to the manager (`Update`/`Remove` return `NotAllowed`); adding a name used by a static connection returns `AlreadyExists`; the empty name is reserved for the static default (`NotAllowed`). `Add`/`Update` open the database first and change nothing on `TestFailed`.
- **Concurrency**: one `SemaphoreSlim` serializes the store write and the cache invalidation (changes are rare). The slow database test runs outside of it.
- **`IConnectionStore`** (`Interfaces/Stores`): `GetAsync`, `SetAsync`, `RemoveAsync` over `ConnectionRecord`s; `IListableConnectionStore` adds `GetNamesAsync` (many distributed stores cannot enumerate). Only dynamic connections go through the store, so static connection secrets never reach a shared store. Replace the default with `UseStore<TStore>()`. A shared implementation is responsible for encrypting connection strings it persists, and can use `IConnectionProvider.OptionsType` to deserialize the options. Changes made through the default in-memory store last until restart.
- **`DynamicConnectionRegistry`** (per-node cache of live services): entries are `Lazy<Task>` so concurrent first calls share one load. A failed load or an unknown name is never cached. `Invalidate(name)` (called by the manager after every change) drops the entry and **retires** the service: if it is `IAsyncDisposable` it is disposed after `RetireDelay` so calls already running finish; otherwise it is simply dropped. `CacheDuration` (default null = never) expires entries so a node re-reads a shared store: this is the only mechanism that bounds cross-node staleness (there is deliberately no messaging/notifier layer).
- **Resolution**: `IConnectionHandlerFactory.GetHandlerAsync(name)` resolves static then dynamic (loading from the store on a miss). There is no synchronous lookup: a dynamic connection can need a store read, so resolution is async only (static connections can also be injected directly). Consumers must not keep a dynamic handler: resolve it on each use. `Names` lists static names plus the dynamic names loaded on this node; use `manager.GetNamesAsync()` for the store's list.
- **`DynamicConnectionOptions`** (`ConfigureDynamicConnections`): `CacheDuration` (`TimeSpan?`, default null) and `RetireDelay` (default 30 s).

### Lifetimes and the stateless contract

`IConnectionHandlerService` implementations are **singletons (static) or cached per node (dynamic), so they must stay stateless and thread-safe**. They keep only their constructor inputs (options, logger) and open a new connection per call. Don't add per-call or per-request mutable state to a provider service, and don't make `Create` depend on scoped services.

### Key types (core)

- **`IConnectionHandlerService`**: the public contract. `CreateDbCommand`, `ExecuteAsync` / `ExecuteNoReturnAsync` (call a routine by name with a parameter dictionary), `ExecuteDbCommandAsync` / `ExecuteDbCommandNoReturnAsync` (run a provider command), `BulkInsertAsync` (from a `DataTable`). Results come back as `IEnumerable<DataTable>`, one per result set. A result set with the same column name twice is rejected on every provider and read path with an `InvalidOperationException` (the `DuplicateNameException` is the inner exception): columns are never silently renamed, the query must alias them. The query has already run on the server when this is thrown.
- **`BaseConnectionHandlerService`**: shared helpers for providers: `ExecuteReaderAsync` (reads all result sets into `DataTable`s), `ExecuteReader` (the synchronous twin, only used by Mssql when the async read switches are off; both share `CreateDataTable` / `AddRow`) and `ExecuteCommandNoReturnInternalAsync` (executes then disposes the command, logs `DbException`).
- **`ConnectionRecord(Name, Provider, Options)`**: the single description of a connection, static or dynamic. `Provider` is the provider id, `Options` the provider-specific `ConnectionHandlerOptions`. It overrides `PrintMembers` so `ToString()` never prints the connection string; this protects logs only, serializers and destructuring loggers still see `Options`. Treat `Options` as immutable once handed over (the in-memory store keeps the reference).
- **`ConnectionHandle`**: record a provider passes to the builder for a **static** connection: `Record`, `Create`, `Test`, optional `ConfigureServices`.
- **`IConnectionProvider` / `BaseConnectionProvider<TOptions>`**: turns a `ConnectionRecord` into a live service for dynamic connections (`Provider` id, `OptionsType`, `Create`, `TestAsync`). Providers derive from the base class, which does the options cast once (wrong type throws `ArgumentException`).
- **`ConnectionHandlerOptions`**: abstract base: `ConnectionString`, optional `ConnectionHandlerHealthCheckOptions`, virtual `Validate()`.
- **`ConnectionHandlerHealthCheckOptions`**: name, failure status, query, timeout, tags. A null options object means no health check is registered. Defaults come from each provider's `<P>ProviderDefaults` (`HealthCheckQuery` `"SELECT 1;"`, `HealthCheckTag` `"database"`, `HealthCheckName`); failure status defaults to `Unhealthy`. `ResolveName` gives the provider default name, suffixed `:<name>` for named connections, unless `Name` is set. Health checks apply to static connections only.
- **`BulkInsertOptions`**: abstract base; each provider has its own subclass, and passing another provider's options throws `ArgumentException`.

### Adding a provider

Follow the Mssql layout:
- `Models/<P>ConnectionHandlerOptions` (+ `<P>BulkInsertOptions` if useful).
- `Models/<P>ProviderDefaults`: `public static class` with `Provider` (**`const`**: the id is persisted inside stored `ConnectionRecord`s, so it must never change) and `static readonly` `HealthCheckName`, `HealthCheckQuery`, `HealthCheckTag`. Public so callers can write `new ConnectionRecord(name, MssqlProviderDefaults.Provider, options)`.
- `Services/<P>ConnectionHandlerService` (`internal sealed partial`, extends `BaseConnectionHandlerService`, implements `IConnectionHandlerService`).
- `Services/<P>ConnectionProvider` (`internal sealed`, extends `BaseConnectionProvider<<P>ConnectionHandlerOptions>`). If the service owns a disposable resource when created dynamically, make the service `IAsyncDisposable` so the registry can retire it.
- `Extensions/<P>ConnectionHandlerExtensions` with `Add<P>Provider()` and the two `Add<P>` overloads (the latter pass the options into the `ConnectionRecord` and chain `.Add<P>Provider()`).

The provider id is now functional: it selects the `IConnectionProvider` for a dynamic `ConnectionRecord`. For static connections it is informational.

## Provider notes

- **Mssql**: `ExecuteAsync` runs the procedure with `CommandType.StoredProcedure`. Reads have two paths. The default is the **legacy fallback**: the synchronous reader (`ExecuteReader`), which blocks the calling thread for the whole query and honors the cancellation token only before the command starts. It is used unless the app sets both `Switch.Microsoft.Data.SqlClient.UseCompatibilityAsyncBehaviour` and `Switch.Microsoft.Data.SqlClient.UseCompatibilityProcessSni` to `false` at startup, which selects the async reader (`ExecuteReaderAsync`, packet multiplexing, opt-in preview). Setting the switches is strongly recommended for throughput under load; the sample sets them. Bulk insert uses `SqlBulkCopy` (`MssqlBulkInsertOptions.BulkCopyOptions`). Dynamic: `SqlClient` pools by connection string and expires idle connections itself, so nothing is disposed on retire.
- **Postgres**: no `CommandType.StoredProcedure`. `ExecuteAsync` builds `SELECT * FROM routine(a => @a, ...)` for **functions** that return data; `ExecuteNoReturnAsync` builds `CALL routine(...)` for **procedures**. Parameters are passed by name, so dictionary keys must match the argument names. Void functions and `INOUT` procedures go through `CreateDbCommand` + `ExecuteDbCommand*`. Routine, table and column names (and parameter names) are validated against the Postgres identifier rules before being put in the SQL; quoted identifiers are accepted for routine/table/column names. A new `NpgsqlConnection` is opened per call; Npgsql pools by connection string and closes idle connections itself, so nothing is disposed on retire (the pool entry stays in Npgsql's global registry; `NpgsqlConnection.ClearPool` is the explicit cleanup). No `NpgsqlDataSource` is used, to pool like the other providers. Bulk insert uses binary `COPY` (`PostgresBulkInsertOptions.ColumnTypes` overrides inferred types).
- **Sqlite**: SQLite has no stored routines, so `ExecuteAsync` / `ExecuteNoReturnAsync` run the "routine name" as plain SQL text (parameters are bound by name). `CreateDbCommand` only accepts `CommandType.Text` (any other type throws `ArgumentException`). `Microsoft.Data.Sqlite` async methods run synchronously. Bulk insert is one transaction with a reused parameterized `INSERT`; it accepts no bulk options. A new connection is opened per call, so a plain `:memory:` database is empty on every call; use a file or a shared memory database. The default open mode creates a missing file, so the startup test does not catch a wrong path unless the connection string uses `Mode=ReadWrite`.
- **Mysql**: `MySqlConnector`. `ExecuteAsync` / `ExecuteNoReturnAsync` run the routine with `CommandType.StoredProcedure`. Parameter names are prefixed with `@`. A new `MySqlConnection` is opened per call; MySqlConnector pools by connection string and closes idle connections itself (`ConnectionIdleTimeout`), so nothing is disposed on retire. Bulk insert uses `MySqlBulkCopy` (`LOAD DATA LOCAL INFILE`: the connection string must allow local infile and the server must enable `local_infile`); it accepts no bulk options.

## Conventions

- Log messages use `[LoggerMessage]` source-generated partial methods, prefixed `"DB - "`, with a provider-specific exception overload and a generic `Exception` overload.
- Providers log and rethrow; they never swallow exceptions. The manager is the exception: database failures while adding/updating a dynamic connection are reported in the `ConnectionChangeResult`.
- Code is organised with `#region` blocks (`Command`, `Connection`, `Stored Procedure`, `Bulk Insert`, `Logs`).
- `ExecuteDbCommand*` methods reject a command that is not the provider's own type with `ArgumentException`, and take ownership of (dispose) the command.
- Types that own a disposable field (e.g. a `SemaphoreSlim`) must be disposable themselves: CA1001 is an error in Release.
- Public members carry XML doc comments.
- Library code (the core and the providers) calls `.ConfigureAwait(false)` on every `await`, so it never resumes on a caller's UI or request context (WinForms, WPF, MAUI). The `await using` statements are the exception: their implicit `DisposeAsync` cannot take it without wrapping the resource. Tests and the sample do not need it.

## Tests

```bash
dotnet test Bacon.DBConnectionHandler.Tests/Bacon.DBConnectionHandler.Tests.csproj
```

No database server is needed: the suite runs offline.

- `Core/`: builder, factory, manager, `DynamicConnectionRegistry`, in-memory store, options, records. They run on `Fakes/TestHost`, which builds a real service provider around `FakeConnectionProvider` (counts what it is asked to do, can be told to fail its test) and takes an optional store and clock (`ManualTimeProvider`, which only fakes the timestamp). `RecordingConnectionStore.GetGate` keeps a load in flight to test cancellation and invalidation races.
- `Providers/`: provider ids and defaults, registration, the parts of the Mssql, Postgres and Mysql services that never reach a server (`CreateDbCommand`, wrong command type, identifier validation), and the real behavior of Sqlite over a temporary file (`Fakes/SqliteTestDatabase`, pooling off so the file can be deleted). `UnreachableDatabaseTests` points the server providers at `127.0.0.1:1`, which is refused at once, to check `TestFailed` and the `Build()` failure.
- Disposal: `Core/DynamicConnectionRegistryDisposalTests` covers a provider's services that are async-only, sync-only, both (async is used) or not disposable, the registry's `Dispose()` / `DisposeAsync()`, a `Dispose()` that must not deadlock on a blocked synchronization context, and the container disposing the registry either way. The fake services of `Fakes/FakeConnectionHandlerService.cs` exist for those cases.
- Test method names are PascalCase without underscores (CA1707 is on and must stay clean), e.g. `AddAsyncReturnsAlreadyExistsForAStaticName`: subject, then scenario, then expected result. No warning is suppressed in the Tests project.
- NUnit 5: `Assert.ThrowsAsync` / `CatchAsync` return a `Task` and must be awaited; use `Assert.MultipleAsync` when the body awaits.
- Dispose the service provider with `await using`: `DynamicConnectionRegistry` only implements `IAsyncDisposable`, so a synchronous `Dispose()` of a provider that created it throws.

## Sample app configuration

The sample reads DB credentials from `appsettings.Development.json` (or user secrets via `UserSecretsId`) under the `DatabaseSettings` section: `MSSQL:DatabaseServer`, `MSSQL:DatabaseUsername`, `MSSQL:DatabasePassword`, `MSSQL:DatabaseName`, the same four keys under `PostGres:`, the keys `DatabaseServer`, `DatabasePort`, `DatabaseUsername`, `DatabasePassword`, `DatabaseName` under `MySql:` (read, but the static Mysql connection is commented out in `Program.cs`), and `Sqlite:DatabasePath`.
