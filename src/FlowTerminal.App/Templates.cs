using System.IO;
using System.Text.Json;

namespace FlowTerminal.App;

/// <summary>
/// A saved chart layout: the timeframe, price-series style, CVD display mode, and the
/// set of enabled indicators. Drawings are intentionally excluded — they are anchored
/// to specific bar times and do not transfer meaningfully between sessions.
/// </summary>
public sealed record ChartTemplate(
    string Name,
    int TimeframeMinutes,
    string ChartType,
    string CvdView,
    string[] Indicators)
{
    /// <summary>Footprint cell mode (added in v0.37; older templates default to Bid×Ask).</summary>
    public string FootprintMode { get; init; } = "BidAsk";

    /// <summary>Read-only DOM base preset (added in v0.42; older templates default to Full Professional).</summary>
    public string DomPreset { get; init; } = "Full Professional";

    /// <summary>
    /// Serialised custom DOM column layout (added in v0.43): show/hide, order, and widths.
    /// Null means "use the preset as-is"; older templates have no custom layout.
    /// </summary>
    public string? DomLayout { get; init; }
}

/// <summary>
/// Loads and saves chart templates as JSON under the user's application-data folder.
/// All failures degrade to an empty list / no-op so a corrupt file never blocks the UI.
/// </summary>
public sealed class TemplateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public TemplateStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowTerminal");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "templates.json");
    }

    public List<ChartTemplate> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<ChartTemplate>();
            return JsonSerializer.Deserialize<List<ChartTemplate>>(File.ReadAllText(_path)) ?? new List<ChartTemplate>();
        }
        catch
        {
            return new List<ChartTemplate>();
        }
    }

    public void Save(List<ChartTemplate> templates)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(templates, Options));
        }
        catch
        {
            // Persisting templates is best-effort; never surface an I/O error to the UI.
        }
    }
}
