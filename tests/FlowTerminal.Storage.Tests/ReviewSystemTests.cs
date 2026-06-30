using FlowTerminal.Domain.Instruments;
using FlowTerminal.Notes;
using FlowTerminal.Storage.Sqlite;
using Xunit;

namespace FlowTerminal.Storage.Tests;

public sealed class ReviewSystemTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDatabase _db;

    public ReviewSystemTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ft-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new SqliteDatabase(Path.Combine(_dir, "review.db"));
        _db.Migrate();
    }

    [Fact]
    public void Schema_Upgrades_To_Version_2()
    {
        Assert.Equal(2, _db.GetSchemaVersion());
    }

    [Fact]
    public async Task Bookmarks_Persist_And_Query_By_Root()
    {
        var repo = new SqliteBookmarkRepository(_db);
        var b = new Bookmark(Guid.NewGuid(), DateTime.UtcNow, new DateTime(2024, 6, 3, 14, 30, 0, DateTimeKind.Utc),
            RootSymbol.NQ, "NQZ5", "Open drive");
        await repo.AddAsync(b, default);

        var nq = await repo.QueryAsync(new ReviewQuery { Root = RootSymbol.NQ }, default);
        Assert.Single(nq);
        Assert.Equal(new DateTime(2024, 6, 3, 14, 30, 0, DateTimeKind.Utc), nq[0].ReplayTimestampUtc);

        var es = await repo.QueryAsync(new ReviewQuery { Root = RootSymbol.ES }, default);
        Assert.Empty(es);
    }

    [Fact]
    public async Task Manual_Annotation_Persists_And_Is_Flagged_Unverified()
    {
        var repo = new SqliteAnnotationRepository(_db);
        var a = new ManualAnnotation(Guid.NewGuid(), DateTime.UtcNow, RootSymbol.ES, "ESZ5",
            TradeDirection.Long, EntryPriceTicks: 84000, ExitPriceTicks: 84040,
            StopPriceTicks: 83980, TargetPriceTicks: 84080, TradeOutcome.Win, "manual review note");
        await repo.AddAsync(a, default);

        var rows = await repo.QueryAsync(new ReviewQuery { Root = RootSymbol.ES }, default);
        Assert.Single(rows);
        Assert.True(rows[0].IsManualUnverified);              // never broker-confirmed
        Assert.Equal(4.0, rows[0].RiskRewardRatio);           // reward 80 / risk 20 ticks
    }

    [Fact]
    public async Task Notes_Query_Filters_By_Date_Range()
    {
        var repo = new SqliteNotesRepository(_db);
        await repo.AddAsync(new SessionNote(Guid.NewGuid(), new DateTime(2024, 6, 3, 12, 0, 0, DateTimeKind.Utc), RootSymbol.NQ, "NQZ5", "day 1", new[] { "a" }), default);
        await repo.AddAsync(new SessionNote(Guid.NewGuid(), new DateTime(2024, 6, 5, 12, 0, 0, DateTimeKind.Utc), RootSymbol.NQ, "NQZ5", "day 3", new[] { "b" }), default);

        var only3rd = await repo.QueryAsync(
            new ReviewQuery { FromDate = new DateOnly(2024, 6, 3), ToDate = new DateOnly(2024, 6, 3) }, default);
        Assert.Single(only3rd);
        Assert.Equal("day 1", only3rd[0].Text);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class ReviewExporterTests
{
    [Fact]
    public void Notes_Csv_Escapes_Commas_And_Quotes()
    {
        var notes = new[]
        {
            new SessionNote(Guid.Empty, new DateTime(2024, 6, 3, 0, 0, 0, DateTimeKind.Utc), RootSymbol.NQ, "NQZ5", "had a \"comma, inside\"", new[] { "setup", "review" }),
        };

        string csv = ReviewExporter.NotesToCsv(notes);
        Assert.Contains("\"had a \"\"comma, inside\"\"\"", csv);
        Assert.Contains("setup|review", csv);
        Assert.StartsWith("Id,TimestampUtc,Root,Contract,Tags,Text", csv);
    }

    [Fact]
    public void Annotations_Csv_Marks_Manual_Unverified()
    {
        var a = new[]
        {
            new ManualAnnotation(Guid.Empty, DateTime.UtcNow, RootSymbol.ES, "ESZ5", TradeDirection.Short,
                84000, null, 84020, 83940, TradeOutcome.Open, "thesis"),
        };

        string csv = ReviewExporter.AnnotationsToCsv(a);
        Assert.Contains("ManualUnverified", csv);
        Assert.Contains(",true,", csv); // the flag column is always true
    }

    [Fact]
    public void Notes_Json_Round_Trips_Shape()
    {
        var notes = new[] { new SessionNote(Guid.NewGuid(), DateTime.UtcNow, RootSymbol.NQ, "NQZ5", "t", new[] { "x" }) };
        string json = ReviewExporter.NotesToJson(notes);
        Assert.Contains("\"Text\": \"t\"", json);
    }
}

public sealed class ScreenshotStoreTests : IDisposable
{
    private readonly string _dir;
    public ScreenshotStoreTests() => _dir = Path.Combine(Path.GetTempPath(), "ft-shots-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Saves_And_Lists_Screenshots()
    {
        var store = new ScreenshotStore(_dir);
        var refA = store.Save(new byte[] { 1, 2, 3 }, RootSymbol.NQ, "NQZ5", new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc));
        Assert.True(File.Exists(refA.FilePath));
        Assert.EndsWith(".png", refA.FilePath);
        Assert.Single(store.List());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
