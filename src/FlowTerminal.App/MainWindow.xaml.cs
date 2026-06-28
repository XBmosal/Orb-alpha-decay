using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlowTerminal.Charting.Studies;
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
    private readonly StudyState _studyState = new();
    private readonly DispatcherTimer _renderTimer;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyViewModel();

        Chart.Attach(_studyState);
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 FPS
        _renderTimer.Tick += OnRenderTick;

        BuildStudiesPanel();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Populates the Studies tab from <see cref="StudyCatalog"/>, grouped by category.
    /// Detector-backed studies toggle the live detector engine; others show their
    /// honest status (Active / Ready / Planned). This is observational analysis only.
    /// </summary>
    private void BuildStudiesPanel()
    {
        foreach (StudyCategory category in Enum.GetValues<StudyCategory>())
        {
            StudiesPanel.Children.Add(new TextBlock
            {
                Text = StudyCatalog.CategoryTitle(category),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 4),
                Foreground = (Brush)FindResource("TextBrush"),
            });

            foreach (var study in StudyCatalog.ByCategory(category))
            {
                StudiesPanel.Children.Add(BuildStudyRow(study));
            }
        }
    }

    private FrameworkElement BuildStudyRow(StudyDefinition study)
    {
        var row = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };

        var toggle = new CheckBox
        {
            Content = study.Name,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = study.Status != StudyStatus.Planned,
            IsEnabled = study.Status != StudyStatus.Planned,
            ToolTip = study.Description,
        };

        // Chart-overlay studies are keyed by short code; detector-backed studies also
        // toggle the live detector engine. Seed the initial state from the checkbox.
        _studyState.Set(study.ShortCode, toggle.IsChecked == true);
        void Apply(bool on)
        {
            _studyState.Set(study.ShortCode, on);
            if (study.DetectorKey is { } key) _feed.SetDetectorEnabled(key, on);
        }

        toggle.Checked += (_, _) => Apply(true);
        toggle.Unchecked += (_, _) => Apply(false);

        DockPanel.SetDock(toggle, Dock.Left);
        row.Children.Add(toggle);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = StatusBrush(study.Status),
            Child = new TextBlock
            {
                Text = StudyCatalog.StatusLabel(study.Status),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12)),
            },
        };
        DockPanel.SetDock(chip, Dock.Right);
        row.Children.Add(chip);

        return row;
    }

    private static Brush StatusBrush(StudyStatus status) => status switch
    {
        StudyStatus.Active => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),     // green
        StudyStatus.EngineReady => new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)), // cyan
        _ => new SolidColorBrush(Color.FromRgb(0x8B, 0x90, 0x9A)),                       // muted
    };

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var contract = new ContractCalendar().SuggestActive(RootSymbol.NQ, DateOnly.FromDateTime(DateTime.UtcNow));
        ContractText.Text = contract.FullSymbol;
        await _feed.StartAsync(contract);
        Heatmap.Attach(_feed);
        _renderTimer.Start();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var snapshot = _feed.Snapshot();
        Chart.UpdateFrame(snapshot.Bars, snapshot.Overlays);
        Dom.UpdateRows(snapshot.Dom);
        CvdText.Text = snapshot.Cvd.ToString("N0");
        BookStateText.Text = snapshot.BookValid ? "Book: valid" : $"Book: INVALID ({snapshot.BookInvalidReason})";
        TapeText.Text = FormatTape(snapshot.Tape);
        Heatmap.InvalidateVisual(); // repaint the heatmap from the feed's tiled history

        var d = snapshot.Diagnostics;
        string latest = snapshot.Detections.Count > 0 ? $" · last: {snapshot.Detections[0].Label}" : string.Empty;
        DiagnosticsText.Text =
            $"events {d.EventsProcessed:N0} · queue {d.CurrentQueueDepth} · dropped {d.DroppedCanonicalEvents} · gaps {d.SequenceGaps} · signals {snapshot.TotalDetections:N0}{latest}";
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
