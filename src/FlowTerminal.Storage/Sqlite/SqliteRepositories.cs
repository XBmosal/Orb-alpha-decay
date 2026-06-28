using System.Text.Json;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Notes;
using Microsoft.Data.Sqlite;

namespace FlowTerminal.Storage.Sqlite;

/// <summary>SQLite-backed application settings.</summary>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SqliteDatabase _db;

    public SqliteSettingsRepository(SqliteDatabase db) => _db = db;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        var result = cmd.ExecuteScalar();
        return Task.FromResult(result as string);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }
}

/// <summary>SQLite-backed workspace storage.</summary>
public sealed class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly SqliteDatabase _db;

    public SqliteWorkspaceRepository(SqliteDatabase db) => _db = db;

    public Task SaveAsync(string name, string json, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workspaces(name, json, updated_utc) VALUES($n, $j, $u)
            ON CONFLICT(name) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string name, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM workspaces WHERE name = $n;";
        cmd.Parameters.AddWithValue("$n", name);
        return Task.FromResult(cmd.ExecuteScalar() as string);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM workspaces ORDER BY name;";
        var names = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return Task.FromResult<IReadOnlyList<string>>(names);
    }
}

/// <summary>SQLite-backed session-review notes (manual annotations; never broker-confirmed trades).</summary>
public sealed class SqliteNotesRepository : INotesRepository, INotesQueryRepository
{
    private readonly SqliteDatabase _db;

    public SqliteNotesRepository(SqliteDatabase db) => _db = db;

    public Task AddAsync(SessionNote note, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes(id, timestamp_utc, root, contract, text, tags)
            VALUES($id, $ts, $root, $contract, $text, $tags)
            ON CONFLICT(id) DO UPDATE SET text = excluded.text, tags = excluded.tags;
            """;
        cmd.Parameters.AddWithValue("$id", note.Id.ToString());
        cmd.Parameters.AddWithValue("$ts", note.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$root", note.Root.ToString());
        cmd.Parameters.AddWithValue("$contract", note.ContractSymbol);
        cmd.Parameters.AddWithValue("$text", note.Text);
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(note.Tags));
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionNote>> QueryAsync(RootSymbol? root, string? tag, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, timestamp_utc, root, contract, text, tags FROM notes" +
                          (root is null ? string.Empty : " WHERE root = $root") +
                          " ORDER BY timestamp_utc;";
        if (root is not null)
        {
            cmd.Parameters.AddWithValue("$root", root.Value.ToString());
        }

        var results = new List<SessionNote>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? new List<string>();
            if (tag is not null && !tags.Contains(tag))
            {
                continue;
            }

            results.Add(new SessionNote(
                Guid.Parse(reader.GetString(0)),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Enum.Parse<RootSymbol>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                tags));
        }

        return Task.FromResult<IReadOnlyList<SessionNote>>(results);
    }

    /// <summary>Richer query: filter by root, contract, and date range (tag filtered in-memory).</summary>
    public Task<IReadOnlyList<SessionNote>> QueryAsync(ReviewQuery query, CancellationToken cancellationToken)
    {
        using var connection = _db.OpenConnection();
        using var cmd = connection.CreateCommand();
        var where = new System.Text.StringBuilder("1=1");
        ReviewSql.ApplyDateRoot(where, cmd, query, "timestamp_utc");
        cmd.CommandText = $"SELECT id, timestamp_utc, root, contract, text, tags FROM notes WHERE {where} ORDER BY timestamp_utc;";

        var results = new List<SessionNote>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? new List<string>();
            if (query.Tag is not null && !tags.Contains(query.Tag))
            {
                continue;
            }

            results.Add(new SessionNote(
                Guid.Parse(reader.GetString(0)),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Enum.Parse<RootSymbol>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                tags));
        }

        return Task.FromResult<IReadOnlyList<SessionNote>>(results);
    }
}
