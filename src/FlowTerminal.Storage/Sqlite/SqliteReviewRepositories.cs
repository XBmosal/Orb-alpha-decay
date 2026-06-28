using System.Globalization;
using System.Text;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Notes;
using Microsoft.Data.Sqlite;

namespace FlowTerminal.Storage.Sqlite;

internal static class ReviewSql
{
    public static void ApplyDateRoot(StringBuilder where, SqliteCommand cmd, ReviewQuery q, string tsColumn)
    {
        if (q.Root is not null) { where.Append(" AND root = $root"); cmd.Parameters.AddWithValue("$root", q.Root.Value.ToString()); }
        if (q.ContractSymbol is not null) { where.Append(" AND contract = $contract"); cmd.Parameters.AddWithValue("$contract", q.ContractSymbol); }
        if (q.FromDate is { } f) { where.Append($" AND {tsColumn} >= $from"); cmd.Parameters.AddWithValue("$from", f.ToDateTime(TimeOnly.MinValue).ToString("O")); }
        if (q.ToDate is { } t) { where.Append($" AND {tsColumn} < $to"); cmd.Parameters.AddWithValue("$to", t.AddDays(1).ToDateTime(TimeOnly.MinValue).ToString("O")); }
    }

    public static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

public sealed class SqliteBookmarkRepository : IBookmarkRepository
{
    private readonly SqliteDatabase _db;
    public SqliteBookmarkRepository(SqliteDatabase db) => _db = db;

    public Task AddAsync(Bookmark b, CancellationToken cancellationToken)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bookmarks(id, created_utc, replay_utc, root, contract, label)
            VALUES($id,$c,$r,$root,$contract,$label)
            ON CONFLICT(id) DO UPDATE SET label = excluded.label, replay_utc = excluded.replay_utc;
            """;
        cmd.Parameters.AddWithValue("$id", b.Id.ToString());
        cmd.Parameters.AddWithValue("$c", b.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$r", b.ReplayTimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$root", b.Root.ToString());
        cmd.Parameters.AddWithValue("$contract", b.ContractSymbol);
        cmd.Parameters.AddWithValue("$label", b.Label);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Bookmark>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        var where = new StringBuilder("1=1");
        ReviewSql.ApplyDateRoot(where, cmd, query, "created_utc");
        cmd.CommandText = $"SELECT id, created_utc, replay_utc, root, contract, label FROM bookmarks WHERE {where} ORDER BY replay_utc;";

        var list = new List<Bookmark>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Bookmark(
                Guid.Parse(r.GetString(0)), ReviewSql.ParseUtc(r.GetString(1)), ReviewSql.ParseUtc(r.GetString(2)),
                Enum.Parse<RootSymbol>(r.GetString(3)), r.GetString(4), r.GetString(5)));
        }

        return Task.FromResult<IReadOnlyList<Bookmark>>(list);
    }
}

public sealed class SqliteAnnotationRepository : IAnnotationRepository
{
    private readonly SqliteDatabase _db;
    public SqliteAnnotationRepository(SqliteDatabase db) => _db = db;

    public Task AddAsync(ManualAnnotation a, CancellationToken cancellationToken)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO annotations(id, created_utc, root, contract, direction, entry_ticks, exit_ticks, stop_ticks, target_ticks, outcome, notes)
            VALUES($id,$c,$root,$contract,$dir,$entry,$exit,$stop,$target,$outcome,$notes)
            ON CONFLICT(id) DO UPDATE SET outcome = excluded.outcome, exit_ticks = excluded.exit_ticks, notes = excluded.notes;
            """;
        cmd.Parameters.AddWithValue("$id", a.Id.ToString());
        cmd.Parameters.AddWithValue("$c", a.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$root", a.Root.ToString());
        cmd.Parameters.AddWithValue("$contract", a.ContractSymbol);
        cmd.Parameters.AddWithValue("$dir", a.Direction.ToString());
        cmd.Parameters.AddWithValue("$entry", a.EntryPriceTicks);
        cmd.Parameters.AddWithValue("$exit", (object?)a.ExitPriceTicks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$stop", (object?)a.StopPriceTicks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$target", (object?)a.TargetPriceTicks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$outcome", a.Outcome.ToString());
        cmd.Parameters.AddWithValue("$notes", a.Notes);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ManualAnnotation>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        var where = new StringBuilder("1=1");
        ReviewSql.ApplyDateRoot(where, cmd, query, "created_utc");
        cmd.CommandText = $"SELECT id, created_utc, root, contract, direction, entry_ticks, exit_ticks, stop_ticks, target_ticks, outcome, notes FROM annotations WHERE {where} ORDER BY created_utc;";

        var list = new List<ManualAnnotation>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ManualAnnotation(
                Guid.Parse(r.GetString(0)), ReviewSql.ParseUtc(r.GetString(1)),
                Enum.Parse<RootSymbol>(r.GetString(2)), r.GetString(3),
                Enum.Parse<TradeDirection>(r.GetString(4)), r.GetInt64(5),
                r.IsDBNull(6) ? null : r.GetInt64(6),
                r.IsDBNull(7) ? null : r.GetInt64(7),
                r.IsDBNull(8) ? null : r.GetInt64(8),
                Enum.Parse<TradeOutcome>(r.GetString(9)), r.GetString(10)));
        }

        return Task.FromResult<IReadOnlyList<ManualAnnotation>>(list);
    }
}
