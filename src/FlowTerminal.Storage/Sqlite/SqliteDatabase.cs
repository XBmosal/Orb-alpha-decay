using Microsoft.Data.Sqlite;

namespace FlowTerminal.Storage.Sqlite;

/// <summary>
/// Owns the application metadata SQLite database (settings, workspaces, drawings,
/// notes, bookmarks, schema version). Applies forward-only migrations keyed off
/// <c>PRAGMA user_version</c> so an existing database upgrades in place and a
/// half-applied migration can be detected. WAL mode is enabled for crash safety.
/// </summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;

    public SqliteDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    /// <summary>Applies any pending migrations. Idempotent and safe to call on startup.</summary>
    public void Migrate()
    {
        using var connection = OpenConnection();
        int version = GetUserVersion(connection);

        if (version < 1)
        {
            ExecuteScript(connection, """
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS workspaces (
                    name       TEXT PRIMARY KEY,
                    json       TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS notes (
                    id           TEXT PRIMARY KEY,
                    timestamp_utc TEXT NOT NULL,
                    root         TEXT NOT NULL,
                    contract     TEXT NOT NULL,
                    text         TEXT NOT NULL,
                    tags         TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_notes_root ON notes(root);
                """);
            SetUserVersion(connection, 1);
        }
    }

    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();
        return GetUserVersion(connection);
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteScript(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
