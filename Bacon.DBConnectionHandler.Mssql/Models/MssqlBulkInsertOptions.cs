using Bacon.DBConnectionHandler.Models.Options;
using Microsoft.Data.SqlClient;

namespace Bacon.DBConnectionHandler.Mssql.Models;

/// <summary>
/// MSSQL bulk insert options
/// </summary>
public sealed class MssqlBulkInsertOptions : BulkInsertOptions
{
    /// <summary>
    /// The SqlBulkCopy options. (default value : SqlBulkCopyOptions.Default)
    /// </summary>
    public SqlBulkCopyOptions BulkCopyOptions { get; set; } = SqlBulkCopyOptions.Default;
}