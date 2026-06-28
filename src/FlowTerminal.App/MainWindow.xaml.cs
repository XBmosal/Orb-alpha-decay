using System.Text;
using System.Windows;
using System.Windows.Threading;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.App;

/// <summary>
/// The main shell window. It binds read-only state from <see cref="ShellViewModel"/>
/// and renders a live mock feed. There are deliberately no buy/sell/cancel/flatten/
/// reverse controls anywhere in this window or its code-behind.
///
/// Rendering is decoupled from ingestion: a 30 FPS dispatcher timer samples the
/// latest coalesced snapshot from <see cref="LiveFeedService"/>; the feed processes
/// canonical events on its own thread and never drops them to keep up with the UI.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly LiveFeedService _feed = new();
    private readonly DispatcherTimer _renderTimer;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyViewModel();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 FPS
        _renderTimer.Tick += OnRenderTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var contract = new ContractCalendar().SuggestActive(RootSymbol.NQ, DateOnly.FromDateTime(DateTime.UtcNow));
        ContractText.Text = contract.FullSymbol;
        await _feed.StartAsync(contract);
        _renderTimer.Start();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var snapshot = _feed.Snapshot();
        Chart.UpdateBars(snapshot.Bars);
        Dom.UpdateRows(snapshot.Dom);
        CvdText.Text = snapshot.Cvd.ToString("N0");
        BookStateText.Text = snapshot.BookValid ? "Book: valid" : $"Book: INVALID ({snapshot.BookInvalidReason})";
        TapeText.Text = FormatTape(snapshot.Tape);

        var d = snapshot.Diagnostics;
        DiagnosticsText.Text =
            $"events {d.EventsProcessed:N0} · queue {d.CurrentQueueDepth} · dropped {d.DroppedCanonicalEvents} · gaps {d.SequenceGaps}";
    }

    private static string FormatTape(IReadOnlyList<Charting.Tape.TapeRow> rows)
    {
        var sb = new StringBuilder();
        int n = Math.Min(12, rows.Count);
        for (int i = 0; i < n; i++)
        {
            var r = rows[i];
            string side = r.Aggressor == Domain.Events.AggressorSide.Buy ? "B" : r.Aggressor == Domain.Events.AggressorSide.Sell ? "S" : "·";
            sb.AppendLine($"{r.ExchangeTimestampUtc:HH:mm:ss}  {side}  {r.Quantity,4}{(r.IsLarge ? "  *" : string.Empty)}");
        }

        return sb.ToString();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        await _feed.DisposeAsync();
    }

    private void ApplyViewModel()
    {
        InstrumentText.Text = _viewModel.SelectedInstrument;
        ContractText.Text = "(discovering…)";
        SessionText.Text = _viewModel.SessionLabel;
        SimulatedBanner.Text = _viewModel.SimulatedDataBanner;
        ReadOnlyBanner.Text = _viewModel.ReadOnlyBanner;
        RithmicStatusText.Text = _viewModel.RithmicStatus;
    }
}
