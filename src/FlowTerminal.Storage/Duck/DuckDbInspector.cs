using DuckDB.NET.Data;

namespace FlowTerminal.Storage.Duck;

public readonly record struct EventTypeCount(byte Type, long Count);

/// <summary>
/// Read-only DuckDB queries over recorded Parquet sessions. DuckDB reads the part
/// files directly via a glob, so inspection and exports never load the whole
/// session into managed memory.
/// </summary>
public static class DuckDbInspector
{
    /// <summary>Total recorded events across a session directory's part files.</summary>
    public static long CountEvents(string sessionDirectory)
    {
        string glob = Glob(sessionDirectory);
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM read_parquet('{glob}', hive_partitioning=false);";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Per-event-type counts, descending by count.</summary>
    public static IReadOnlyList<EventTypeCount> CountByType(string sessionDirectory)
    {
        string glob = Glob(sessionDirectory);
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT Type, count(*) AS n FROM read_parquet('{glob}', hive_partitioning=false) GROUP BY Type ORDER BY n DESC;";
        var results = new List<EventTypeCount>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EventTypeCount(Convert.ToByte(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1))));
        }

        return results;
    }

    private static string Glob(string sessionDirectory)
    {
        // Forward slashes work for DuckDB on all platforms; escape single quotes.
        var path = Path.Combine(sessionDirectory, "events-*.parquet").Replace('\\', '/');
        return path.Replace("'", "''");
    }
}
