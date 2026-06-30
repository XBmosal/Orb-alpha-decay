namespace FlowTerminal.App;

/// <summary>
/// Which chart-overlay studies are currently enabled (by short code, e.g. "VWAP",
/// "VBP", "FVG", "ORB"). Updated by the Studies panel checkboxes and read by the
/// chart host when painting — both on the UI thread, so no locking is needed.
/// </summary>
public sealed class StudyState
{
    private readonly HashSet<string> _enabled = new();

    public void Set(string code, bool enabled)
    {
        if (enabled) _enabled.Add(code);
        else _enabled.Remove(code);
    }

    public bool IsEnabled(string code) => _enabled.Contains(code);

    public IReadOnlySet<string> Enabled => _enabled;
}
