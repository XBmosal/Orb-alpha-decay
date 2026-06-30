using System.Collections.ObjectModel;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Infrastructure;
using FlowTerminal.Rithmic;

namespace FlowTerminal.App;

/// <summary>
/// Read-only view state for the shell. It exposes only observational data: the
/// supported instruments (NQ and ES only), the active run mode, and honest status
/// banners. There are intentionally no commands for buy/sell/cancel/flatten — the
/// view model has no execution surface to bind to.
/// </summary>
public sealed class ShellViewModel
{
    public ShellViewModel()
    {
        foreach (var spec in InstrumentRegistry.All)
        {
            Instruments.Add(spec.RootSymbol);
        }

        SelectedInstrument = Instruments.Count > 0 ? Instruments[0] : "NQ";
        Mode = RunMode.Mock;
    }

    public string ProductName => AppInfo.ProductName;

    /// <summary>The UI exposes only NQ and ES.</summary>
    public ObservableCollection<string> Instruments { get; } = new();

    public string SelectedInstrument { get; set; }

    public RunMode Mode { get; }

    public string ModeDescription => AppInfo.DescribeMode(Mode);

    /// <summary>Prominent simulated-data banner shown whenever mock data is active.</summary>
    public string SimulatedDataBanner => Mode == RunMode.Mock ? AppInfo.SimulatedDataBanner : string.Empty;

    public bool ShowSimulatedBanner => Mode != RunMode.Rithmic;

    public string ReadOnlyBanner => AppInfo.ReadOnlyBanner;

    public string RithmicStatus => RithmicAvailability.StatusDescription;

    public string SessionLabel => "RTH · America/New_York";
}
