using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace Bacon.DBConnectionHandler.Postgres.Services;

internal sealed partial class PostgresConnectionHandlerService(PostgresConnectionHandlerOptions postgresConnectionHandlerOptions, ILogger<PostgresConnectionHandlerService> logger) : BaseConnectionHandlerService(logger), IConnectionHandlerService
{
    #region Command

    public DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        return CreateCommandInternal(commandText, commandType, commandTimeout: commandTimeout, parameters: parameters);
    }

    private static NpgsqlCommand CreateCommandInternal(string commandText, CommandType commandType, NpgsqlConnection? npgsqlConnection = null, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        NpgsqlCommand npgsqlCommand = new()
        {
            CommandType = commandType,
            CommandText = commandText,
            CommandTimeout = commandTimeout
        };

        if (npgsqlConnection is not null)
        {
            npgsqlCommand.Connection = npgsqlConnection;
        }

        if (parameters is not null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                npgsqlCommand.Parameters.Add(new NpgsqlParameter(NormalizeParameterName(parameter.Key), parameter.Value ?? DBNull.Value));
            }
        }

        return npgsqlCommand;
    }

    #endregion Command

    #region Connection

    private async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            NpgsqlConnection npgsqlConnection = new(postgresConnectionHandlerOptions.ConnectionString);
            await npgsqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return npgsqlConnection;
        }
        catch (NpgsqlException npgsqlEx)
        {
            LogGetConnectionNpgsqlException(npgsqlEx);
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
        await using NpgsqlConnection npgsqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteCommandInternalAsync(CreateRoutineCommand(routineName, isProcedure: false, npgsqlConnection, parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not NpgsqlCommand npgsqlCommand)
        {
            throw new ArgumentException("DB - Postgres requires a NpgsqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using NpgsqlConnection npgsqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        npgsqlCommand.Connection = npgsqlConnection;

        return await ExecuteCommandInternalAsync(npgsqlCommand, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<DataTable>> ExecuteCommandInternalAsync(NpgsqlCommand npgsqlCommand, CancellationToken cancellationToken)
    {
        try
        {
            await using (npgsqlCommand)
            {
                return await ExecuteReaderAsync(npgsqlCommand, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (NpgsqlException npgsqlEx)
        {
            LogExecuteCommandInternalNpgsqlException(npgsqlEx);
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
        await using NpgsqlConnection npgsqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteCommandNoReturnInternalAsync(CreateRoutineCommand(routineName, isProcedure: true, npgsqlConnection, parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not NpgsqlCommand npgsqlCommand)
        {
            throw new ArgumentException("DB - Postgres requires a NpgsqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using NpgsqlConnection npgsqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        npgsqlCommand.Connection = npgsqlConnection;

        await ExecuteCommandNoReturnInternalAsync(npgsqlCommand, cancellationToken).ConfigureAwait(false);
    }

    #endregion No Return

    #endregion Stored Procedure

    #region Bulk Insert

    public async Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default)
    {
        if (bulkInsertOptions is not null and not PostgresBulkInsertOptions)
        {
            throw new ArgumentException("DB - Postgres requires PostgresBulkInsertOptions.", nameof(bulkInsertOptions));
        }

        ArgumentNullException.ThrowIfNull(data);

        ValidateIdentifier(destinationTable, nameof(destinationTable));

        IDictionary<string, NpgsqlDbType>? columnTypes = (bulkInsertOptions as PostgresBulkInsertOptions)?.ColumnTypes;

        // Source DataTable column -> destination column and its explicit type, if any (resolved once here, not once per cell). Without a mapping, every column goes to the destination column of the same name
        List<(DataColumn Source, string Destination, NpgsqlDbType? DbType)> columns = [];

        if (columnsMapping is null)
        {
            foreach (DataColumn dataColumn in data.Columns)
            {
                string destination = ValidateIdentifier(dataColumn.ColumnName, nameof(columnsMapping));

                columns.Add((dataColumn, destination, ResolveColumnType(columnTypes, destination)));
            }
        }
        else
        {
            // Only the mapped columns are inserted
            foreach (DataColumn dataColumn in data.Columns)
            {
                if (columnsMapping.TryGetValue(dataColumn.ColumnName, out string? mappedName))
                {
                    string destination = ValidateIdentifier(mappedName, nameof(columnsMapping));

                    columns.Add((dataColumn, destination, ResolveColumnType(columnTypes, destination)));
                }
            }
        }

        try
        {
            await using NpgsqlConnection npgsqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using NpgsqlBinaryImporter writer = await npgsqlConnection.BeginBinaryImportAsync($"COPY {destinationTable} ({string.Join(", ", columns.Select(s => s.Destination))}) FROM STDIN (FORMAT BINARY)", cancellationToken).ConfigureAwait(false);

            // 0 means no limit, like SqlBulkCopy
            writer.Timeout = bulkInsertTimeout > 0 ? TimeSpan.FromSeconds(bulkInsertTimeout) : Timeout.InfiniteTimeSpan;

            foreach (DataRow dataRow in data.Rows)
            {
                await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);

                foreach ((DataColumn dataColumn, _, NpgsqlTypes.NpgsqlDbType? npgsqlDbType) in columns)
                {
                    object value = dataRow[dataColumn];

                    if (npgsqlDbType is { } explicitType)
                    {
                        await writer.WriteAsync(value is DBNull ? null : value, explicitType, cancellationToken).ConfigureAwait(false);
                    }
                    else if (value is DBNull)
                    {
                        await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException npgsqlEx)
        {
            LogBulkInsertNpgsqlException(npgsqlEx);
            throw;
        }
        catch (Exception ex)
        {
            LogBulkInsertException(ex);
            throw;
        }
    }

    #endregion Bulk Insert

    #region Private

    /// <summary>
    /// Builds "SELECT * FROM routine(a => @a, b => @b)" (function) or "CALL routine(a => @a, b => @b)" (procedure).
    /// Parameters are passed by name, so the dictionary keys must match the routine argument names.
    /// </summary>
    private static NpgsqlCommand CreateRoutineCommand(string routineName, bool isProcedure, NpgsqlConnection npgsqlConnection, IDictionary<string, object?>? parameters)
    {
        ValidateIdentifier(routineName, nameof(routineName));

        StringBuilder commandText = new(isProcedure ? "CALL " : "SELECT * FROM ");
        commandText.Append(routineName).Append('(');

        if (parameters is not null)
        {
            commandText.AppendJoin(", ", parameters.Keys.Select(key =>
            {
                string name = NormalizeParameterName(key);

                if (!ParameterNameRegex().IsMatch(name))
                {
                    throw new ArgumentException($"DB - Invalid parameter name '{key}'.", nameof(parameters));
                }

                return $"{name} => @{name}";
            }));
        }

        commandText.Append(')');

        return CreateCommandInternal(commandText.ToString(), CommandType.Text, npgsqlConnection, parameters: parameters);
    }

    private static string NormalizeParameterName(string name) => name.TrimStart('@', ':');

    private static string ValidateIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex().IsMatch(identifier))
        {
            throw new ArgumentException($"DB - Invalid identifier '{identifier}'. Expected [schema.]name, optionally double quoted.", paramName);
        }

        return identifier;
    }

    private static NpgsqlDbType? ResolveColumnType(IDictionary<string, NpgsqlDbType>? columnTypes, string destination)
    {
        return columnTypes is not null && columnTypes.TryGetValue(destination.Trim('"'), out NpgsqlTypes.NpgsqlDbType npgsqlDbType) ? npgsqlDbType : null;
    }

    #endregion Private

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A postgres exception occured while getting a postgres connection.")]
    private partial void LogGetConnectionNpgsqlException(NpgsqlException npgsqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while getting a postgres connection.")]
    private partial void LogGetConnectionException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A postgres exception occured while executing a routine.")]
    private partial void LogExecuteCommandInternalNpgsqlException(NpgsqlException npgsqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a routine.")]
    private partial void LogExecuteCommandInternalException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A postgres exception occured while executing a bulk insert.")]
    private partial void LogBulkInsertNpgsqlException(NpgsqlException npgsqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a bulk insert.")]
    private partial void LogBulkInsertException(Exception exception);

    #endregion Logs

    #region Regex

    // Identifier rules, source: https://www.postgresql.org/docs/current/sql-syntax-lexical.html (4.1.1 Identifiers and Key Words)
    // - Unquoted: starts with a letter (including letters with diacritical marks and non-Latin letters) or an underscore,
    //   followed by letters, underscores, digits or dollar signs. Folded to lower case by Postgres.
    // - Quoted: any character except code zero, "" escapes a quote. Case sensitive.
    // Qualified names (schema.name, database.schema.name): https://www.postgresql.org/docs/current/ddl-schemas.html
    // The 63 bytes limit, the reserved key words and the U&"..." form are not checked here, Postgres reports them
    [GeneratedRegex("""^(?:[\p{L}_][\p{L}\p{Nd}_$]*|"(?:[^"\0]|"")+")(?:\.(?:[\p{L}_][\p{L}\p{Nd}_$]*|"(?:[^"\0]|"")+"))*$""")]
    private static partial Regex IdentifierRegex();

    // A named argument (name => value) is an unquoted identifier, same rules as above
    // Source: https://www.postgresql.org/docs/current/sql-syntax-calling-funcs.html
    [GeneratedRegex("""^[\p{L}_][\p{L}\p{Nd}_$]*$""")]
    private static partial Regex ParameterNameRegex();

    #endregion Regex
}