using Bacon.DBConnectionHandler.Models.Options;
using NpgsqlTypes;

namespace Bacon.DBConnectionHandler.Postgres.Models;

/// <summary>
/// Postgres bulk insert options (binary COPY)
/// </summary>
public sealed class PostgresBulkInsertOptions : BulkInsertOptions
{
    /// <summary>
    /// Explicit Postgres types by destination column name. Binary COPY is strict about types, so use this when the
    /// type inferred from the DataTable column does not match the destination column (e.g. int sent to a bigint, string sent to a uuid or jsonb).
    /// Columns not listed use the type inferred from the value. (default value : null)
    /// </summary>
    public IDictionary<string, NpgsqlDbType>? ColumnTypes { get; set; }
}