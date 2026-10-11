using Bacon.DBConnectionHandler.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Data.Common;

namespace Bacon.DBConnectionHandler.Tests.Core;

/// <summary>
/// The shared readers of the base service, run over an in-memory SQLite connection. The synchronous reader is only used by Mssql,
/// which cannot run offline, so it is covered here through a minimal derived class
/// </summary>
[TestFixture]
public class BaseConnectionHandlerServiceTests
{
    private sealed class ReaderService() : BaseConnectionHandlerService(NullLogger.Instance)
    {
        public static Task<List<DataTable>> ReadAsync(DbCommand dbCommand, CancellationToken cancellationToken = default)
        {
            return ExecuteReaderAsync(dbCommand, cancellationToken);
        }

        public static List<DataTable> Read(DbCommand dbCommand, CancellationToken cancellationToken = default)
        {
            return ExecuteReader(dbCommand, cancellationToken);
        }
    }

    private SqliteConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
    }

    private SqliteCommand NewCommand(string sql)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;

        return command;
    }

    #region Async

    [Test]
    public async Task ExecuteReaderAsyncReadsEveryResultSet()
    {
        List<DataTable> tables = await ReaderService.ReadAsync(NewCommand("select 1 as a; select 'x' as b, null as c;"));

        Assert.Multiple(() =>
        {
            Assert.That(tables, Has.Count.EqualTo(2));
            Assert.That(tables[0].Rows[0]["a"], Is.EqualTo(1L));
            Assert.That(tables[1].Rows[0]["b"], Is.EqualTo("x"));
            Assert.That(tables[1].Rows[0]["c"], Is.EqualTo(DBNull.Value));
        });
    }

    [Test]
    public async Task ExecuteReaderAsyncThrowsWhenTheResultSetRepeatsAColumnName()
    {
        InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await ReaderService.ReadAsync(NewCommand("select 1 as id, 2 as id")));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'id'"));
            Assert.That(exception.InnerException, Is.InstanceOf<DuplicateNameException>());
        });
    }

    #endregion Async

    #region Sync

    [Test]
    public void ExecuteReaderReadsEveryResultSet()
    {
        List<DataTable> tables = ReaderService.Read(NewCommand("select 1 as a; select 'x' as b, null as c;"));

        Assert.Multiple(() =>
        {
            Assert.That(tables, Has.Count.EqualTo(2));
            Assert.That(tables[0].Rows[0]["a"], Is.EqualTo(1L));
            Assert.That(tables[1].Rows[0]["b"], Is.EqualTo("x"));
            Assert.That(tables[1].Rows[0]["c"], Is.EqualTo(DBNull.Value));
        });
    }

    [Test]
    public void ExecuteReaderReturnsAnEmptyTableForAQueryWithoutRows()
    {
        List<DataTable> tables = ReaderService.Read(NewCommand("select 1 as a where 0"));

        Assert.Multiple(() =>
        {
            Assert.That(tables, Has.Count.EqualTo(1));
            Assert.That(tables[0].Columns, Has.Count.EqualTo(1));
            Assert.That(tables[0].Rows, Is.Empty);
        });
    }

    [Test]
    public void ExecuteReaderThrowsWhenTheResultSetRepeatsAColumnName()
    {
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => ReaderService.Read(NewCommand("select 1 as id, 2 as id")));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("'id'"));
            Assert.That(exception.InnerException, Is.InstanceOf<DuplicateNameException>());
        });
    }

    [Test]
    public void ExecuteReaderThrowsWhenTheTokenIsAlreadyCancelled()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        Assert.Catch<OperationCanceledException>(() => ReaderService.Read(NewCommand("select 1"), cancellationTokenSource.Token));
    }

    #endregion Sync
}
