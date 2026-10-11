using Bacon.DBConnectionHandler.Mssql.Models;
using Bacon.DBConnectionHandler.Mysql.Models;
using Bacon.DBConnectionHandler.Postgres.Models;
using Bacon.DBConnectionHandler.Sqlite.Models;

namespace Bacon.DBConnectionHandler.Tests.Providers;

[TestFixture]
public class ProviderDefaultsTests
{
    [Test]
    public void ProviderIsThePersistedProviderId()
    {
        // These ids are stored inside ConnectionRecords: changing one orphans every stored record of that provider
        Assert.Multiple(() =>
        {
            Assert.That(MssqlProviderDefaults.Provider, Is.EqualTo("mssql"));
            Assert.That(PostgresProviderDefaults.Provider, Is.EqualTo("postgres"));
            Assert.That(SqliteProviderDefaults.Provider, Is.EqualTo("sqlite"));
            Assert.That(MysqlProviderDefaults.Provider, Is.EqualTo("mysql"));
        });
    }

    [Test]
    public void ProviderIsDifferentForEveryProvider()
    {
        string[] providers = [MssqlProviderDefaults.Provider, PostgresProviderDefaults.Provider, SqliteProviderDefaults.Provider, MysqlProviderDefaults.Provider];

        Assert.That(providers.Distinct().Count(), Is.EqualTo(providers.Length));
    }

    [Test]
    public void HealthCheckNameIsDifferentForEveryProvider()
    {
        string[] names = [MssqlProviderDefaults.HealthCheckName, PostgresProviderDefaults.HealthCheckName, SqliteProviderDefaults.HealthCheckName, MysqlProviderDefaults.HealthCheckName];

        Assert.Multiple(() =>
        {
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "two providers share a health check name");
            Assert.That(MssqlProviderDefaults.HealthCheckName, Is.EqualTo("sqlserver"));
            Assert.That(PostgresProviderDefaults.HealthCheckName, Is.EqualTo("postgres"));
            Assert.That(SqliteProviderDefaults.HealthCheckName, Is.EqualTo("sqlite"));
            Assert.That(MysqlProviderDefaults.HealthCheckName, Is.EqualTo("mysql"));
        });
    }

    [Test]
    public void HealthCheckQueryIsASimpleSelect()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MssqlProviderDefaults.HealthCheckQuery, Is.EqualTo("SELECT 1;"));
            Assert.That(PostgresProviderDefaults.HealthCheckQuery, Is.EqualTo("SELECT 1;"));
            Assert.That(SqliteProviderDefaults.HealthCheckQuery, Is.EqualTo("SELECT 1;"));
            Assert.That(MysqlProviderDefaults.HealthCheckQuery, Is.EqualTo("SELECT 1;"));
        });
    }

    [Test]
    public void HealthCheckTagIsDatabase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MssqlProviderDefaults.HealthCheckTag, Is.EqualTo("database"));
            Assert.That(PostgresProviderDefaults.HealthCheckTag, Is.EqualTo("database"));
            Assert.That(SqliteProviderDefaults.HealthCheckTag, Is.EqualTo("database"));
            Assert.That(MysqlProviderDefaults.HealthCheckTag, Is.EqualTo("database"));
        });
    }
}
