using Bacon.DBConnectionHandler.Models.Options;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Interfaces.Services;

/// <summary>
/// Manages the SQL connection
/// </summary>
public interface IConnectionHandlerService
{
    #region Sql Command

    /// <summary>
    /// Creates an Sql command
    /// </summary>
    /// <param name="commandText">The stored procedude name, the name of the table or the SQL query to run</param>
    /// <param name="commandType">The SQL command type (stored procedure, text</param>
    /// <param name="commandTimeout">The time in seconds to wait for the command to execute. The default is 30 seconds.</param>
    /// <param name="parameters">A dictionary containing the paramaters to pass to the stored procedure. Key is the paramater name, Value is a nullable object. Default value is null</param>
    /// <returns>A base DbCommand</returns>
    DbCommand CreateDbCommand(string commandText, CommandType commandType, int commandTimeout = 30, IDictionary<string, object?>? parameters = null);

    #endregion Sql Command

    #region Stored Procedure

    #region With Return

    /// <summary>
    /// Executes the stored procedure returning the data sets of the stored procedure
    /// For MSSQL : executes a stored procedure via exec as usual
    /// For Postgres : wraps a select around the function and pass the parameters if any
    /// </summary>
    /// <param name="routineName">The full routine name including it's schema (EG: schema + procedure name in mssql)</param>
    /// <param name="parameters">A dictionary containing the parameters in the form of Key=name of the parameter, Value=object representing the value</param>
    /// <param name="cancellationToken">The optional cancellation token</param>
    /// <returns>A list of DataTable containing the data sets requested</returns>
    /// <exception cref="DbException">Throws the SQL exception linked to the cause of the issue</exception>
    /// <exception cref="Exception">Throws the exception linked to the cause of the issue</exception>
    Task<IEnumerable<DataTable>> ExecuteAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query in the database based on the CommandType. 
    /// If the CommandType used is Text, it will try to execute the SQL query passed in the CommandText
    /// If the CommandType used is StoredProcedure, the command will try to use the CommandText as the stored procedure name.
    /// If the CommandType used is TableDirect, the DbCommand expects the name of the table to modify through the command
    /// </summary>
    /// <param name="dbCommand">The sql command to execute</param>
    /// <param name="cancellationToken">The optional cancellation token</param>
    /// <returns>A list of DataTable containing the data sets requested</returns>
    /// <exception cref="DbException">Throws the SQL exception linked to the cause of the issue</exception>
    /// <exception cref="Exception">Throws the exception linked to the cause of the issue</exception>
    Task<IEnumerable<DataTable>> ExecuteDbCommandAsync(DbCommand dbCommand, CancellationToken cancellationToken = default);

    #endregion With Return

    #region No Return

    /// <summary>
    /// Executes the stored procedure and return nothing
    /// For MSSQL : executes a stored procedure via exec as usual
    /// For Postgres : executes a stored procedure via CALL. For void function refer to <seealso cref="ExecuteDbCommandNoReturnAsync"/>
    /// </summary>
    /// <param name="routineName">The full routine name including it's schema</param>
    /// <param name="parameters">A dictionary containing the paramaters to pass to the stored procedure. Key is the paramater name, Value is a nullable object. Default value is null</param>
    /// <param name="cancellationToken">The optional cancellation token</param>
    /// <exception cref="DbException">Throws the SQL exception linked to the cause of the issue</exception>
    /// <exception cref="Exception">Throws the exception linked to the cause of the issue</exception>
    Task ExecuteNoReturnAsync(string routineName, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the stored procedure or sql query using the Command.Text. 
    /// If the CommandType used is Text, it will try to execute the SQL query passed in the CommandText
    /// If the CommandType used is StoredProcedure, the command will try to use the CommandText as the stored procedure name.
    /// If the CommandType used is TableDirect, the DbCommand expects the name of the table to modify through the command
    /// </summary>
    /// <param name="dbCommand">The sql command to execute</param>
    /// <param name="cancellationToken">The optional cancellation token</param>
    /// <exception cref="DbException">Throws the SQL exception linked to the cause of the issue</exception>
    /// <exception cref="Exception">Throws the exception linked to the cause of the issue</exception>
    Task ExecuteDbCommandNoReturnAsync(DbCommand dbCommand, CancellationToken cancellationToken = default);

    #endregion No Return

    #endregion Stored Procedure

    #region Bulk Insert

    /// <summary>
    /// This method bulk load a SQL Server received table with the received data.
    /// </summary>
    /// <param name="data">A System.Data.DataTable that represents the data which will be inserted in the database.</param>
    /// <param name="destinationTable">A System.String that represents the table's destination.</param>
    /// <param name="columnsMapping">A System.Collections.Generic.Dictionary that represents the columns mapping. Default value is null</param>
    /// <param name="bulkInsertTimeout">A System.Int the time in seconds before the connection timeouts. Default value is 30 seconds. A value of 0 indicates no limit; the bulk copy will wait indefinitely.</param>
    /// <param name="bulkInsertOptions">Provider specific options (e.g. MssqlBulkInsertOptions). Null uses the provider defaults</param>
    /// <param name="cancellationToken">The optional cancellation token</param>
    /// <returns></returns>
    /// <exception cref="DbException">Throws the SQL exception linked to the cause of the issue</exception>
    /// <exception cref="Exception">Throws the exception linked to the cause of the issue</exception>
    Task BulkInsertAsync(DataTable data, string destinationTable, Dictionary<string, string>? columnsMapping = null, int bulkInsertTimeout = 30, BulkInsertOptions? bulkInsertOptions = null, CancellationToken cancellationToken = default);

    #endregion Bulk Insert
}