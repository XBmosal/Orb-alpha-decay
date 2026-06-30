using FlowTerminal.Domain.Instruments;
using FlowTerminal.Notes;
using FlowTerminal.Storage.Sqlite;
using Xunit;

namespace FlowTerminal.Storage.Tests;

public sealed class SqliteRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SqliteRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ft-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "flowterminal.db");
    }

    [Fact]
    public void Migrate_Sets_Schema_Version()
    {
        var db = new SqliteDatabase(_dbPath);
        db.Migrate();
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, db.GetSchemaVersion());

        // Idempotent: running again does not change the version or throw.
        db.Migrate();
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
    }

    [Fact]
    public async Task Settings_Round_Trip_And_Upsert()
    {
        var db = new SqliteDatabase(_dbPath);
        db.Migrate();
        var repo = new SqliteSettingsRepository(db);

        await repo.SetAsync("timezone", "America/New_York", default);
        Assert.Equal("America/New_York", await repo.GetAsync("timezone", default));

        await repo.SetAsync("timezone", "UTC", default); // upsert
        Assert.Equal("UTC", await repo.GetAsync("timezone", default));
        Assert.Null(await repo.GetAsync("missing", default));
    }

    [Fact]
    public async Task Workspaces_Save_Load_List()
    {
        var db = new SqliteDatabase(_dbPath);
        db.Migrate();
        var repo = new SqliteWorkspaceRepository(db);

        await repo.SaveAsync("default", "{\"a\":1}", default);
        await repo.SaveAsync("scalping", "{\"b\":2}", default);
        Assert.Equal("{\"a\":1}", await repo.LoadAsync("default", default));

        var list = await repo.ListAsync(default);
        Assert.Equal(new[] { "default", "scalping" }, list);
    }

    [Fact]
    public async Task Notes_Add_And_Query_By_Root_And_Tag()
    {
        var db = new SqliteDatabase(_dbPath);
        db.Migrate();
        var repo = new SqliteNotesRepository(db);

        await repo.AddAsync(new SessionNote(Guid.NewGuid(), DateTime.UtcNow, RootSymbol.NQ, "NQZ5", "breakout", new[] { "setup" }), default);
        await repo.AddAsync(new SessionNote(Guid.NewGuid(), DateTime.UtcNow, RootSymbol.ES, "ESZ5", "absorption", new[] { "review" }), default);

        Assert.Single(await repo.QueryAsync(RootSymbol.NQ, null, default));
        Assert.Single(await repo.QueryAsync(null, "review", default));
        Assert.Equal(2, (await repo.QueryAsync(null, null, default)).Count);
    }

    [Fact]
    public async Task Data_Survives_Reopen_Crash_Recovery()
    {
        var db1 = new SqliteDatabase(_dbPath);
        db1.Migrate();
        await new SqliteSettingsRepository(db1).SetAsync("k", "v", default);

        // Simulate restart: a brand-new database object over the same file.
        var db2 = new SqliteDatabase(_dbPath);
        db2.Migrate(); // should not wipe anything
        Assert.Equal("v", await new SqliteSettingsRepository(db2).GetAsync("k", default));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
