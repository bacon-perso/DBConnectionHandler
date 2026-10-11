using Microsoft.Data.Sqlite;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A real SQLite database in a temporary file, deleted with the object. Pooling is off so the file is never kept open
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bacon-dbch-" + Guid.NewGuid().ToString("N"));

    public SqliteTestDatabase()
    {
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "test.db");
    }

    public string Path { get; }

    /// <summary>
    /// Opens the file, creating it if it does not exist yet
    /// </summary>
    public string ConnectionString => Build(Path, SqliteOpenMode.ReadWriteCreate);

    /// <summary>
    /// Points to a file that does not exist and refuses to create it, so opening it fails
    /// </summary>
    public string MissingFileConnectionString => Build(System.IO.Path.Combine(_directory, "missing.db"), SqliteOpenMode.ReadWrite);

    private static string Build(string path, SqliteOpenMode mode)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ToString();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
