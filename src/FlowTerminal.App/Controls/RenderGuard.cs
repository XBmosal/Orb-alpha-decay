using Serilog;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Throttled logging for isolated render faults. The Skia panels can repaint many
/// times per second; if a panel is faulting we want one diagnostic, then at most one
/// every 30 s per panel — never a flood that buries the log or stalls the UI thread.
/// </summary>
internal static class RenderGuard
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DateTime> LastLogUtc = new();
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static void LogThrottled(string panel)
    {
        var now = DateTime.UtcNow;
        bool log;
        lock (Sync)
        {
            log = !LastLogUtc.TryGetValue(panel, out var last) || now - last >= Interval;
            if (log) LastLogUtc[panel] = now;
        }

        if (log)
        {
            Log.Warning("Render fault isolated in the {Panel} panel; the view is paused but the terminal continues.", panel);
        }
    }
}
