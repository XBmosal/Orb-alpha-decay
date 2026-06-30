namespace FlowTerminal.Infrastructure;

/// <summary>
/// Resolves the on-disk locations the app uses, rooted under the per-user local
/// application-data directory so nothing is written to the install location.
/// Directories are created on demand.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ShortName);

        Data = Path.Combine(Root, "data");
        Logs = Path.Combine(Root, "logs");
        Workspaces = Path.Combine(Root, "workspaces");
        Screenshots = Path.Combine(Root, "screenshots");
        Database = Path.Combine(Root, "flowterminal.db");
    }

    public string Root { get; }
    public string Data { get; }
    public string Logs { get; }
    public string Workspaces { get; }
    public string Screenshots { get; }
    public string Database { get; }

    public AppPaths EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Workspaces);
        Directory.CreateDirectory(Screenshots);
        return this;
    }
}
