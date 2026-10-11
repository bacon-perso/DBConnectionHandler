using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Mysql.Services;

internal sealed partial class MysqlConnectionHandlerService(MysqlConnectionHandlerOptions mysqlConnectionHandlerOptions, ILogger<MysqlConnectionHandlerService> logger)
    : BaseConnectionHandlerService(logger), IConnectionHandlerService
{
    #region Command

    public DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        return CreateCommandInternal(commandText, commandType, mySqlConnection: null, commandTimeout: commandTimeout, parameters: parameters);
    }

    private static MySqlCommand CreateCommandInternal(string commandText, CommandType commandType, MySqlConnection? mySqlConnection = null, int commandTimeout = 30, IDictionary<string, object?>? parameters = null)
    {
        MySqlCommand mySqlCommand = new()
        {
            CommandType = commandType,
            CommandText = commandText,
            CommandTimeout = commandTimeout
        };

        if (mySqlConnection is not null)
        {
            mySqlCommand.Connection = mySqlConnection;
        }

        if (parameters is not null)
        {
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                mySqlCommand.Parameters.AddWithValue(parameter.Key.StartsWith('@') ? parameter.Key : $"@{parameter.Key}", parameter.Value ?? DBNull.Value);
            }
        }

        return mySqlCommand;
    }

    #endregion Command

    #region Connection

    private async Task<MySqlConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            MySqlConnection mySqlConnection = new(mysqlConnectionHandlerOptions.ConnectionString);
            await mySqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return mySqlConnection;
        }
        catch (MySqlException mySqlEx)
        {
            LogGetConnectionMySqlException(mySqlEx);
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
        await using MySqlConnection mySqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ExecuteCommandInternalAsync(CreateCommandInternal(routineName, CommandType.StoredProcedure, mySqlConnection: mySqlConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not MySqlCommand mySqlCommand)
        {
            throw new ArgumentException("DB - MySQL requires a MySqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using MySqlConnection mySqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        mySqlCommand.Connection = mySqlConnection;

        return await ExecuteCommandInternalAsync(mySqlCommand, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<List<DataTable>> ExecuteCommandInternalAsync(MySqlCommand mySqlCommand, CancellationToken cancellationToken)
    {
        try
        {
            await using (mySqlCommand)
            {
                return await ExecuteReaderAsync(mySqlCommand, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (MySqlException mySqlEx)
        {
            LogExecuteCommandInternalMySqlException(mySqlEx);
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
        await using MySqlConnection mySqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteCommandNoReturnInternalAsync(CreateCommandInternal(routineName, CommandType.StoredProcedure, mySqlConnection: mySqlConnection, parameters: parameters), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
    {
        if (dbCommand is not MySqlCommand mySqlCommand)
        {
            throw new ArgumentException("DB - MySQL requires a MySqlCommand. Use CreateDbCommand() to create one.", nameof(dbCommand));
        }

        await using MySqlConnection mySqlConnection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        mySqlCommand.Connection = mySqlConnection;

        await ExecuteCommandNoReturnInternalAsync(mySqlCommand, cancellationToken).ConfigureAwait(false);
    }

    #endregion No Return

    #endregion Stored Procedure

    #region Bulk Insert

    public async Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default)
    {
        if (bulkInsertOptions is not null)
        {
            throw new ArgumentException("DB - MySQL does not support bulk insert options.", nameof(bulkInsertOptions));
        }

        ArgumentNullException.ThrowIfNull(data);

        try
        {
            // MySqlBulkCopy uses LOAD DATA LOCAL INFILE, which the connection must explicitly allow (the server must also have local_infile enabled)
            MySqlConnectionStringBuilder mySqlConnectionStringBuilder = new(mysqlConnectionHandlerOptions.ConnectionString)
            {
                AllowLoadLocalInfile = true
            };

            await using MySqlConnection mySqlConnection = new(mySqlConnectionStringBuilder.ConnectionString);
            await mySqlConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            MySqlBulkCopy mySqlBulkCopy = new(mySqlConnection)
            {
                DestinationTableName = destinationTable,
                BulkCopyTimeout = bulkInsertTimeout
            };

            if (columnsMapping is not null)
            {
                foreach (KeyValuePair<string, string> column in columnsMapping)
                {
                    DataColumn sourceColumn = data.Columns[column.Key] ?? throw new ArgumentException($"DB - The column '{column.Key}' does not exist in the DataTable.", nameof(columnsMapping));

                    mySqlBulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(sourceColumn.Ordinal, column.Value));
                }
            }

            await mySqlBulkCopy.WriteToServerAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException mySqlEx)
        {
            LogBulkInsertMySqlException(mySqlEx);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A MySQL exception occured while getting a MySQL connection.")]
    private partial void LogGetConnectionMySqlException(MySqlException mySqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while getting a MySQL connection.")]
    private partial void LogGetConnectionException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A MySQL exception occured while executing a command.")]
    private partial void LogExecuteCommandInternalMySqlException(MySqlException mySqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a command.")]
    private partial void LogExecuteCommandInternalException(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A MySQL exception occured while executing a bulk insert.")]
    private partial void LogBulkInsertMySqlException(MySqlException mySqlException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a bulk insert.")]
    private partial void LogBulkInsertException(Exception exception);

    #endregion Logs
}