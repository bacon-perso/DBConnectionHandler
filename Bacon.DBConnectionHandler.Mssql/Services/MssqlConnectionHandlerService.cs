using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Mssql.Services;

internal sealed partial class MssqlConnectionHandlerService(MssqlConnectionHandlerOptions mssqlConnectionHandlerOptions, bool useAsyncRead, ILogger<MssqlConnectionHandlerService> logger)
    : BaseConnectionHandlerService(logger), IConnectionHandlerService
{
    #region Command

    public DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        return CreateCommandInternal(commandText, commandType, sqlConnection: null, commandTimeout: commandTimeout, parameters: parameters);
    }

    private static SqlCommand CreateCommandInternal(string commandText, CommandType commandType, SqlConnection? sqlConnection = null, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        SqlCommand sqlCommand = new()
        {
            CommandType = commandType,
            CommandText = commandText,
            CommandTimeout = commandTimeout
        };

        if (sqlConnection is not null)
        {
            sqlCommand.Connection = sqlConnection;
        }

        if (parameters is not null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                sqlCommand.Parameters.AddWithValue(parameter.Key.StartsWith('@') ? parameter.Key : $"@{parameter.Key}", parameter.Value ?? DBNull.Value);
            }
        }

        return sqlCommand;
    }

    #endregion Command

    #region Connection

    private async Task<SqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            SqlConnection sqlConnection = new(mssqlConnectionHandlerOptions.ConnectionString);
            await sqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return sqlConnection;
        }
        catch (SqlException sqlEx)
        {
            LogGetConnectionSqlException(sqlEx);
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
        await using SqlConnection sqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteCommandInternalAsync(CreateCommandInternal(routineName, CommandType.StoredProcedure, sqlConnection: sqlConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not SqlCommand sqlCommand)
        {
            throw new ArgumentException("DB - MSSQL requires a SqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }
            
        await using SqlConnection sqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        sqlCommand.Connection = sqlConnection;

        return await ExecuteCommandInternalAsync(sqlCommand, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<List<DataTable>> ExecuteCommandInternalAsync(SqlCommand sqlCommand, CancellationToken cancellationToken)
    {
        try
        {
            await using (sqlCommand)
            {
                // useAsyncRead selects the packet multiplexing path (opt-in AppContext switches, see MssqlSwitches).
                // Otherwise the synchronous reader runs, which behaves like the old adapter path but throws an exception on duplicate column names
                return useAsyncRead ? await ExecuteReaderAsync(sqlCommand, cancellationToken).ConfigureAwait(false) : ExecuteReader(sqlCommand, cancellationToken);
            }
        }
        catch (SqlException sqlEx)
        {
            LogExecuteCommandInternalSqlException(sqlEx);
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
        await using SqlConnection sqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteCommandNoReturnInternalAsync(CreateCommandInternal(routineName, CommandType.StoredProcedure, sqlConnection: sqlConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not SqlCommand sqlCommand)
        {
            throw new ArgumentException("DB - MSSQL requires a SqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using SqlConnection sqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        sqlCommand.Connection = sqlConnection;

        await ExecuteCommandNoReturnInternalAsync(sqlCommand, cancellationToken).ConfigureAwait(false);
    }

    #endregion No Return

    #endregion Stored Procedure

    #region Bulk Insert

    public async Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default)
    {
        if (bulkInsertOptions is not null and not MssqlBulkInsertOptions)
        {
            throw new ArgumentException("DB - MSSQL requires MssqlBulkInsertOptions.", nameof(bulkInsertOptions));
        }

        try
        {
            SqlBulkCopyOptions sqlBulkCopyOptions = (bulkInsertOptions as MssqlBulkInsertOptions)?.BulkCopyOptions ?? SqlBulkCopyOptions.Default;

            using SqlBulkCopy sqlBulkCopy = new(mssqlConnectionHandlerOptions.ConnectionString, sqlBulkCopyOptions);
            sqlBulkCopy.DestinationTableName = destinationTable;
            sqlBulkCopy.BulkCopyTimeout = bulkInsertTimeout;

            if (columnsMapping != null)
            {
                foreach (KeyValuePair<string, string> column in columnsMapping)
                {
                    sqlBulkCopy.ColumnMappings.Add(column.Key, column.Value);
                }
            }

            await sqlBulkCopy.WriteToServerAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException sqlEx)
        {
            LogBulkInsertSqlException(sqlEx);
            throw;
        }
        catch (Exception ex)
        {
            LogBulkInsertException(ex);
            throw;
        }
    }

    #endregion Bulk Insert

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sql exception occured while getting a sql connection.")]
    private partial void LogGetConnectionSqlException(SqlException sqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while getting a sql connection.")]
    private partial void LogGetConnectionException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sql exception occured while executing a stored procedure.")]
    private partial void LogExecuteCommandInternalSqlException(SqlException sqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a stored procedure.")]
    private partial void LogExecuteCommandInternalException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A sql exception occured while executing a bulk insert.")]
    private partial void LogBulkInsertSqlException(SqlException sqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a bulk insert.")]
    private partial void LogBulkInsertException(Exception exception);

    #endregion Logs
}