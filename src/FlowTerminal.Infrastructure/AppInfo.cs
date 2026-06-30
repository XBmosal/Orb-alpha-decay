namespace FlowTerminal.Infrastructure;

/// <summary>How the terminal is currently sourcing data.</summary>
public enum RunMode
{
    /// <summary>Deterministic synthetic data. No proprietary files required.</summary>
    Mock,

    /// <summary>Deterministic playback of locally recorded sessions.</summary>
    Replay,

    /// <summary>Authorized live Rithmic market data (requires the SDK build profile).</summary>
    Rithmic,
}

/// <summary>
/// Read-only product identity and guarantees. Flow Terminal is strictly a
/// market-data visualization and analysis platform: it never submits, modifies, or
/// cancels orders, connects for execution, or displays accounts/positions/P&amp;L.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "Flow Terminal";

    public const string ShortName = "FlowTerminal";

    /// <summary>Always true. The application has no execution surface of any kind.</summary>
    public const bool IsReadOnly = true;

    /// <summary>Banner shown prominently whenever simulated data is active.</summary>
    public const string SimulatedDataBanner = "SIMULATED DATA";

    /// <summary>Banner reminding the user the app is observational only.</summary>
    public const string ReadOnlyBanner = "READ-ONLY · NO ORDER ENTRY";

    public static string DescribeMode(RunMode mode) => mode switch
    {
        RunMode.Mock => "Mock (Simulated Data)",
        RunMode.Replay => "Local Replay",
        RunMode.Rithmic => "Rithmic Live (Authorized)",
        _ => mode.ToString(),
    };
}
