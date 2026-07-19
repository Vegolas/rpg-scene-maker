using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;

namespace AmbientDirector.Tests.Store;

/// <summary>
/// A real SQLite database backed by a shared in-memory connection, exposed as an
/// <see cref="IDbContextFactory{AppDbContext}"/> so the stores can run against it exactly as in
/// production. Uses <c>Database.Migrate()</c> (not EnsureCreated) because the app applies migrations,
/// and the EF InMemory provider can't handle the model's <c>.ToJson()</c> owned columns / NOCASE.
/// </summary>
public sealed class SqliteTestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var ctx = CreateDbContext();
        ctx.Database.Migrate();
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
