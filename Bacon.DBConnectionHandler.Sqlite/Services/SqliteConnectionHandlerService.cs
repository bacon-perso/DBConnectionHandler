using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Services;
using Bacon.DBConnectionHandler.Sqlite.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace Bacon.DBConnectionHandler.Sqlite.Services;

internal sealed partial class SqliteConnectionHandlerService(SqliteConnectionHandlerOptions sqliteConnectionHandlerOptions, ILogger<SqliteConnectionHandlerService> logger) : BaseConnectionHandlerService(logger), IConnectionHandlerService
{
    private const string _commandTypeNotSupportedMessage = "DB - SQLite only supports CommandType.Text.";

    #region Command

    public DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        return CreateCommandInternal(commandText, commandType, sqliteConnection: null, commandTimeout: commandTimeout, parameters: parameters);
    }

    private static SqliteCommand CreateCommandInternal(string commandText, CommandType commandType, SqliteConnection? sqliteConnection = null, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        // SQLite only supports CommandType.Text, any other type throws an ArgumentException
        if (commandType != CommandType.Text)
        {
            throw new ArgumentException(_commandTypeNotSupportedMessage, nameof(commandType));
        }

        SqliteCommand sqliteCommand = new()
        {
            CommandType = commandType,
            CommandText = commandText,
            CommandTimeout = commandTimeout
        };

        if (sqliteConnection is not null)
        {
            sqliteCommand.Connection = sqliteConnection;
        }

        if (parameters is not null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                // The name is used as is, the $, @ and : prefixes are all matched with or without the prefix
                sqliteCommand.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            }
        }

        return sqliteCommand;
    }

    #endregion Command

    #region Connection

    private async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            SqliteConnection sqliteConnection = new(sqliteConnectionHandlerOptions.ConnectionString);
            await sqliteConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return sqliteConnection;
        }
        catch (SqliteException sqliteEx)
        {
            LogGetConnectionSqliteException(sqliteEx);
            throw;
        }
        catch (Exception ex)
        {
            LogGetConnectionException(ex);
            throw;
        }
    }

    #endregion Connection

    #region Stored Procedure

    #region With Return

    public async Task<IEnumerable<DataTable>> ExecuteAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection sqliteConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteCommandInternalAsync(CreateCommandInternal(routineName, CommandType.Text, sqliteConnection: sqliteConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        // SQLite only supports CommandType.Text, any other type throws an ArgumentException
        if (dbCommand.CommandType != CommandType.Text)
        {
            throw new ArgumentException(_commandTypeNotSupportedMessage);
        }

        if (dbCommand is not SqliteCommand sqliteCommand)
        {
            throw new ArgumentException("DB - SQLite requires a SqliteCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using SqliteConnection sqliteConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        sqliteCommand.Connection = sqliteConnection;

        return await ExecuteCommandInternalAsync(sqliteCommand, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<DataTable>> ExecuteCommandInternalAsync(SqliteCommand sqliteCommand, CancellationToken cancellationToken)
    {
        try
        {
            await using (sqliteCommand)
            {
                // The async calls of Microsoft.Data.Sqlite run synchronously, there is no compatibility/async split like MSSQL
                return await ExecuteReaderAsync(sqliteCommand, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SqliteException sqliteEx)
        {
            LogExecuteCommandInternalSqliteException(sqliteEx);
            throw;
        }
        catch (Exception ex)
        {
            LogExecuteCommandInternalException(ex);
            throw;
        }
    }

    #endregion With Return

    #region No Return

    public async Task ExecuteNoReturnAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection sqliteConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteCommandNoReturnInternalAsync(CreateCommandInternal(routineName, CommandType.Text, sqliteConnection: sqliteConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        // SQLite only supports CommandType.Text, any other type throws an ArgumentException
        if (dbCommand.CommandType != CommandType.Text)
        {
            throw new ArgumentException(_commandTypeNotSupportedMessage);
        }

        if (dbCommand is not SqliteCommand sqliteCommand)
        {
            throw new ArgumentException("DB - SQLite requires a SqliteCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using SqliteConnection sqliteConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        sqliteCommand.Connection = sqliteConnection;

        await ExecuteCommandNoReturnInternalAsync(sqliteCommand, cancellationToken).ConfigureAwait(false);
    }

    #endregion No Return

    #endregion Stored Procedure

    #region Bulk Insert

    // SQLite has no bulk copy API: one transaction and one reused parameterized INSERT (https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/bulk-insert)
    public async Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default)
    {
        if (bulkInsertOptions is not null)
        {
            throw new ArgumentException("DB - SQLite does not support bulk insert options.", nameof(bulkInsertOptions));
        }

        ArgumentNullException.ThrowIfNull(data);
        ValidateIdentifier(destinationTable, nameof(destinationTable));

        // Source DataTable column -> destination column. Without a mapping, every column goes to the destination column of the same name
        List<(DataColumn Source, string Destination)> columns = [];

        if (columnsMapping is null)
        {
            foreach (DataColumn column in data.Columns)
            {
                columns.Add((column, ValidateIdentifier(column.ColumnName, nameof(columnsMapping))));
            }
        }
        else
        {
            // Only the mapped columns are inserted
            foreach (DataColumn column in data.Columns)
            {
                if (columnsMapping.TryGetValue(column.ColumnName, out string? destination))
                {
                    columns.Add((column, ValidateIdentifier(destination, nameof(columnsMapping))));
                }
            }
        }

        try
        {
            await using SqliteConnection sqliteConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction dbTransaction = await sqliteConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
            sqliteCommand.Transaction = (SqliteTransaction)dbTransaction;
            sqliteCommand.CommandTimeout = bulkInsertTimeout;
            sqliteCommand.CommandType = CommandType.Text;
            sqliteCommand.CommandText = $"INSERT INTO {destinationTable} ({string.Join(", ", columns.Select(column => column.Destination))}) VALUES ({string.Join(", ", columns.Select((_, index) => $"@p{index}"))})";

            List<SqliteParameter> sqliteParameters = [];
            for (int index = 0; index < columns.Count; ++index)
            {
                SqliteParameter sqliteParameter = sqliteCommand.CreateParameter();
                sqliteParameter.ParameterName = $"@p{index}";
                sqliteCommand.Parameters.Add(sqliteParameter);
                sqliteParameters.Add(sqliteParameter);
            }

            foreach (DataRow row in data.Rows)
            {
                for (int index = 0; index < columns.Count; ++index)
                {
                    sqliteParameters[index].Value = row[columns[index].Source];
                }

                await sqliteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Disposing the transaction without a commit rolls it back
            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException sqliteEx)
        {
            LogBulkInsertSqliteException(sqliteEx);
            throw;
        }
        catch (Exception ex)
        {
            LogBulkInsertException(ex);
            throw;
        }
    }

    #endregion Bulk Insert

    #region Identifiers

    private static string ValidateIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex().IsMatch(identifier))
        {
            throw new ArgumentException($"DB - Invalid identifier '{identifier}'. Expected [schema.]name, optionally double quoted.", paramName);
        }

        return identifier;
    }

    // Identifier rules, source: https://www.sqlite.org/draft/tokenreq.html
    // - Unquoted: starts with a letter, an underscore or any character above U+007F, followed by letters, digits, underscores, $ or characters above U+007F
    // - Double quoted: any character except code zero, "" escapes a quote
    // - Qualified names: schema.name where schema is main, temp or an attached database (https://www.sqlite.org/lang_createtable.html)
    // The [bracket] and `backtick` quote styles are not accepted here
    [GeneratedRegex("""^(?:[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_$\u0080-\uFFFF]*|"(?:[^"\0]|"")+")(?:\.(?:[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_$\u0080-\uFFFF]*|"(?:[^"\0]|"")+"))?$""")]
    private static partial Regex IdentifierRegex();

    #endregion Identifiers

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sqlite exception occured while getting a sqlite connection.")]
    private partial void LogGetConnectionSqliteException(SqliteException sqliteException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while getting a sqlite connection.")]
    private partial void LogGetConnectionException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sqlite exception occured while executing a command.")]
    private partial void LogExecuteCommandInternalSqliteException(SqliteException sqliteException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a command.")]
    private partial void LogExecuteCommandInternalException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sqlite exception occured while executing a bulk insert.")]
    private partial void LogBulkInsertSqliteException(SqliteException sqliteException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a bulk insert.")]
    private partial void LogBulkInsertException(Exception exception);

    #endregion Logs
}