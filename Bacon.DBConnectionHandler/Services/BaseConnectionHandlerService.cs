using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Services;

/// <summary>
/// Shared helpers for the provider implementations of IConnectionHandlerService
/// </summary>
/// <param name="logger">The logger</param>
public abstract partial class BaseConnectionHandlerService(ILogger logger)
{
    #region Protected

    /// <summary>
    /// Executes the command and reads every result set into a DataTable using the async reader
    /// </summary>
    /// <param name="dbCommand">The database command</param>
    /// <param name="cancellationToken">The cancellation token</param>
    protected static async Task<List<DataTable>> ExecuteReaderAsync(DbCommand dbCommand, CancellationToken cancellationToken)
    {
        List<DataTable> dataTables = [];

        await using DbDataReader dbDataReader = await dbCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        do
        {
            DataTable dataTable = CreateDataTable(dbDataReader);
            object[] values = new object[dbDataReader.FieldCount];

            while (await dbDataReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                AddRow(dataTable, dbDataReader, values);
            }

            dataTables.Add(dataTable);
        }
        while (await dbDataReader.NextResultAsync(cancellationToken).ConfigureAwait(false));

        return dataTables;
    }

    /// <summary>
    /// Synchronous version of <see cref="ExecuteReaderAsync"/>, with the same result.
    /// It blocks the calling thread and only honors the token before the command starts
    /// </summary>
    protected static List<DataTable> ExecuteReader(DbCommand dbCommand, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<DataTable> dataTables = [];

        using DbDataReader dbDataReader = dbCommand.ExecuteReader();

        do
        {
            DataTable dataTable = CreateDataTable(dbDataReader);
            object[] values = new object[dbDataReader.FieldCount];

            while (dbDataReader.Read())
            {
                AddRow(dataTable, dbDataReader, values);
            }

            dataTables.Add(dataTable);
        }
        while (dbDataReader.NextResult());

        return dataTables;
    }

    /// <summary>
    /// Executes the command without reading a result, then disposes the command
    /// </summary>
    /// <param name="dbCommand">The database command</param>
    /// <param name="cancellationToken">The cancellation token</param>
    protected async Task ExecuteCommandNoReturnInternalAsync(DbCommand dbCommand, CancellationToken cancellationToken)
    {
        try
        {
            await using (dbCommand)
            {
                await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (DbException dbEx)
        {
            LogExecuteCommandNoReturnDbException(dbEx);
            throw;
        }
        catch (Exception ex)
        {
            LogExecuteCommandNoReturnException(ex);
            throw;
        }
    }

    #endregion Protected

    #region Private

    private static DataTable CreateDataTable(DbDataReader dbDataReader)
    {
        DataTable dataTable = new();

        for (int idx = 0; idx < dbDataReader.FieldCount; ++idx)
        {
            string columnName = dbDataReader.GetName(idx);

            try
            {
                dataTable.Columns.Add(columnName, dbDataReader.GetFieldType(idx));
            }
            catch (DuplicateNameException ex)
            {
                throw new InvalidOperationException($"DB - The result set contains the column '{columnName}' more than once. Give each column a unique name in the query.", ex);
            }
        }

        return dataTable;
    }

    private static void AddRow(DataTable dataTable, DbDataReader dbDataReader, object[] values)
    {
        dbDataReader.GetValues(values);
        dataTable.Rows.Add(values);
    }

    #region Logs

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - A database exception occured while executing a procedure with no return.")]
    private partial void LogExecuteCommandNoReturnDbException(DbException dbException);

    [LoggerMessage(Level = LogLevel.Error, Message = "DB - An error occured while executing a procedure with no return.")]
    private partial void LogExecuteCommandNoReturnException(Exception exception);

    #endregion Logs

    #endregion Private
}