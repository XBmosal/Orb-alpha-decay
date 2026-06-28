using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowTerminal.Charting.Workspaces;

public sealed record PanelState(string Name, bool Visible, int Order);

/// <summary>
/// Serializable description of a saved workspace: which instrument/contract is
/// selected, display preferences, the chart interval, and panel layout. Persisted
/// as JSON via the workspace repository. UI-only state — it carries no execution,
/// account, or position information.
/// </summary>
public sealed record WorkspaceState
{
    public string SchemaVersion { get; init; } = "1";
    public string SelectedInstrument { get; init; } = "NQ";
    public string? ContractSymbol { get; init; }
    public string DisplayTimeZone { get; init; } = "America/New_York";
    public string SessionTemplate { get; init; } = "RegularTradingHours";
    public string ChartInterval { get; init; } = "1m";
    public bool ReducedMotion { get; init; }
    public int TargetFps { get; init; } = 30;

    public IReadOnlyList<PanelState> Panels { get; init; } = new[]
    {
        new PanelState("MainChart", true, 0),
        new PanelState("Dom", true, 1),
        new PanelState("Cvd", true, 2),
        new PanelState("TimeAndSales", true, 3),
        new PanelState("Diagnostics", true, 4),
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static WorkspaceState FromJson(string json) =>
        JsonSerializer.Deserialize<WorkspaceState>(json, Options)
        ?? throw new InvalidOperationException("Workspace JSON could not be parsed.");
}
