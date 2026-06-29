using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowTerminal.Analytics.Bars;
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
    private Contract? _contract;
    private readonly List<ToggleButton> _timeframeButtons = new();

    // Cached brushes (resolved once) so the per-frame tape rows don't hit FindResource.
    private Brush _buyBrush = Brushes.LimeGreen;
    private Brush _sellBrush = Brushes.MediumPurple;
    private Brush _neutralBrush = Brushes.Gray;
    private Brush _buySoftBrush = Brushes.Transparent;
    private Brush _sellSoftBrush = Brushes.Transparent;
    private Brush _warningBrush = Brushes.Orange;
    private Brush _secondaryBrush = Brushes.Gray;

    /// <summary>The selectable bar timeframes, label → interval. Minutes shown in the title.</summary>
    private static readonly (string Label, TimeSpan Interval)[] Timeframes =
    {
        ("1m", TimeSpan.FromMinutes(1)),
        ("3m", TimeSpan.FromMinutes(3)),
        ("5m", TimeSpan.FromMinutes(5)),
        ("15m", TimeSpan.FromMinutes(15)),
        ("1h", TimeSpan.FromHours(1)),
        ("4h", TimeSpan.FromHours(4)),
    };

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyViewModel();

        Chart.Attach(_studyState);
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 FPS
        _renderTimer.Tick += OnRenderTick;

        CacheBrushes();
        BuildTimeframeBar();
        BuildIndicatorsMenu();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private Brush Brush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private void CacheBrushes()
    {
        _buyBrush = Brush("BullishBrush", _buyBrush);
        _sellBrush = Brush("BearishBrush", _sellBrush);
        _neutralBrush = Brush("TextMutedBrush", _neutralBrush);
        _buySoftBrush = Brush("BullishSoftBrush", _buySoftBrush);
        _sellSoftBrush = Brush("BearishSoftBrush", _sellSoftBrush);
        _warningBrush = Brush("WarningBrush", _warningBrush);
        _secondaryBrush = Brush("TextSecondaryBrush", _secondaryBrush);
    }

    // ── Timeframe selector ──────────────────────────────────────────────────

    private void BuildTimeframeBar()
    {
        foreach (var (label, interval) in Timeframes)
        {
            var btn = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("SegmentedToggleStyle"),
                Tag = interval,
                IsChecked = interval == _feed.Timeframe,
            };

            btn.Click += OnTimeframeClick;
            _timeframeButtons.Add(btn);
            TimeframeBar.Children.Add(btn);
        }
    }

    private async void OnTimeframeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not TimeSpan interval)
        {
            return;
        }

        // Behave like a radio group: the clicked frame stays selected, others clear.
        foreach (var b in _timeframeButtons)
        {
            b.IsChecked = ReferenceEquals(b, clicked);
        }

        UpdateInstrumentTitle();
        await _feed.ChangeTimeframeAsync(interval);
    }

    // ── Indicators menu (replaces the old checkbox list) ────────────────────

    /// <summary>
    /// Populates the Indicators popup from <see cref="StudyCatalog"/>, grouped by
    /// category. Each row is a clickable line that shows a check at the end when the
    /// indicator is selected and nothing when it is not — no checkboxes. Detector-
    /// backed indicators also toggle the live detector engine. Observational only.
    /// </summary>
    private void BuildIndicatorsMenu()
    {
        foreach (StudyCategory category in Enum.GetValues<StudyCategory>())
        {
            IndicatorsPanel.Children.Add(new TextBlock
            {
                Text = StudyCatalog.CategoryTitle(category),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(8, 10, 0, 4),
            });

            foreach (var study in StudyCatalog.ByCategory(category))
            {
                IndicatorsPanel.Children.Add(BuildIndicatorRow(study));
            }
        }
    }

    private FrameworkElement BuildIndicatorRow(StudyDefinition study)
    {
        bool planned = study.Status == StudyStatus.Planned;
        bool on = study.DefaultOn && !planned;

        // Seed the shared study state and any backing detector from the initial value.
        Apply(study, on);

        var check = new TextBlock
        {
            Text = on ? "✓" : string.Empty,
            Foreground = (Brush)FindResource("BullishBrush"),
            FontWeight = FontWeights.Bold,
            Width = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(check, Dock.Right);

        var name = new TextBlock
        {
            Text = study.Name,
            Foreground = (Brush)FindResource(planned ? "MutedTextBrush" : "TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var dock = new DockPanel { Margin = new Thickness(2, 1, 2, 1), LastChildFill = true };
        dock.Children.Add(check);

        // "Planned" indicators are shown but cannot be toggled; tag them faintly.
        if (planned)
        {
            var tag = new TextBlock
            {
                Text = "planned",
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 6, 0),
            };
            DockPanel.SetDock(tag, Dock.Right);
            dock.Children.Add(tag);
        }

        dock.Children.Add(name);

        var row = new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = planned ? Cursors.Arrow : Cursors.Hand,
            Child = dock,
            ToolTip = study.Description,
        };

        if (!planned)
        {
            bool state = on;
            row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("GridBrush");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) =>
            {
                state = !state;
                check.Text = state ? "✓" : string.Empty;
                Apply(study, state);
            };
        }

        return row;
    }

    /// <summary>Applies a study's enabled state to the shared state and any detector.</summary>
    private void Apply(StudyDefinition study, bool on)
    {
        _studyState.Set(study.ShortCode, on);
        if (study.DetectorKey is { } key)
        {
            _feed.SetDetectorEnabled(key, on);
        }
    }

    // ── Lifecycle / render ──────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contract = new ContractCalendar().SuggestActive(RootSymbol.NQ, DateOnly.FromDateTime(DateTime.UtcNow));
        ContractText.Text = _contract.FullSymbol;
        Chart.TickSize = _contract.Spec.TickSize;
        UpdateInstrumentTitle();
        await _feed.StartAsync(_contract);
        Heatmap.Attach(_feed);
        _renderTimer.Start();
    }

    private void UpdateInstrumentTitle()
    {
        if (_contract is null)
        {
            return;
        }

        int minutes = (int)_feed.Timeframe.TotalMinutes;
        InstrumentTitleText.Text = $"{_contract.Spec.Description} Futures";
        ChartHeaderText.Text = $"{_contract.FullSymbol}  ·  {minutes}m  ·  CME";
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var snapshot = _feed.Snapshot();
        Chart.UpdateFrame(snapshot.Bars, snapshot.Overlays);
        Dom.UpdateRows(snapshot.Dom);

        long cvd = snapshot.Cvd;
        CvdText.Text = cvd.ToString("N0");
        CvdText.Foreground = cvd >= 0 ? _buyBrush : _sellBrush;

        UpdateBookState(snapshot.BookValid, snapshot.BookInvalidReason);
        TapeList.ItemsSource = BuildTapeRows(snapshot.Tape);
        UpdateOhlc(snapshot.Bars);
        Heatmap.InvalidateVisual(); // repaint the heatmap from the feed's tiled history

        var d = snapshot.Diagnostics;
        string latest = snapshot.Detections.Count > 0 ? $"  ·  last: {snapshot.Detections[0].Label}" : string.Empty;
        DiagnosticsText.Text =
            $"events {d.EventsProcessed:N0}   ·   queue {d.CurrentQueueDepth}   ·   dropped {d.DroppedCanonicalEvents}   ·   gaps {d.SequenceGaps}   ·   signals {snapshot.TotalDetections:N0}{latest}";
    }

    private void UpdateBookState(bool valid, string? reason)
    {
        if (valid)
        {
            BookStateDot.Fill = _buyBrush;
            BookStateText.Text = "Book Valid";
            BookStateText.Foreground = _secondaryBrush;
            BookStateChip.ToolTip = null;
        }
        else
        {
            BookStateDot.Fill = _warningBrush;
            BookStateText.Text = "Book Invalid";
            BookStateText.Foreground = _warningBrush;
            BookStateChip.ToolTip = reason is null ? "Order book is temporarily invalid." : $"Order book invalid: {reason}";
        }
    }

    /// <summary>Updates the header OHLC readout from the latest bar and the session change.</summary>
    private void UpdateOhlc(IReadOnlyList<Bar> bars)
    {
        if (_contract is null || bars.Count == 0)
        {
            return;
        }

        var spec = _contract.Spec;
        var last = bars[^1];
        decimal open = PriceConverter.ToPrice(spec, last.OpenTicks);
        decimal high = PriceConverter.ToPrice(spec, last.HighTicks);
        decimal low = PriceConverter.ToPrice(spec, last.LowTicks);
        decimal close = PriceConverter.ToPrice(spec, last.CloseTicks);

        LastPriceText.Text = close.ToString("N2", CultureInfo.InvariantCulture);
        OpenText.Text = open.ToString("N2", CultureInfo.InvariantCulture);
        HighText.Text = high.ToString("N2", CultureInfo.InvariantCulture);
        LowText.Text = low.ToString("N2", CultureInfo.InvariantCulture);
        CloseText.Text = close.ToString("N2", CultureInfo.InvariantCulture);

        long sessionVolume = 0;
        foreach (var b in bars) sessionVolume += b.Volume;
        VolumeText.Text = sessionVolume.ToString("N0", CultureInfo.InvariantCulture);

        // Session change: latest close vs the open of the oldest bar in the buffer.
        decimal sessionOpen = PriceConverter.ToPrice(spec, bars[0].OpenTicks);
        decimal change = close - sessionOpen;
        decimal pct = sessionOpen != 0 ? change / sessionOpen * 100m : 0m;
        string sign = change >= 0 ? "+" : "−";
        var dirBrush = change >= 0 ? _buyBrush : _sellBrush;
        ChangeText.Text = $"{sign}{Math.Abs(change).ToString("N2", CultureInfo.InvariantCulture)}";
        ChangePctText.Text = $"{sign}{Math.Abs(pct).ToString("N2", CultureInfo.InvariantCulture)}%";
        ChangeText.Foreground = dirBrush;
        ChangePctText.Foreground = dirBrush;
    }

    /// <summary>Row view-model for the Time &amp; Sales feed (display-only fields).</summary>
    public sealed record TapeRowVm(string Time, string Price, string Size, string Side, Brush SideBrush, Brush RowBackground);

    private List<TapeRowVm> BuildTapeRows(IReadOnlyList<Charting.Tape.TapeRow> rows)
    {
        var spec = _contract!.Spec;
        int n = Math.Min(30, rows.Count);
        var list = new List<TapeRowVm>(n);
        for (int i = 0; i < n; i++)
        {
            var r = rows[i];
            bool buy = r.Aggressor == Domain.Events.AggressorSide.Buy;
            bool sell = r.Aggressor == Domain.Events.AggressorSide.Sell;
            string side = buy ? "B" : sell ? "S" : "·";
            Brush sideBrush = buy ? _buyBrush : sell ? _sellBrush : _neutralBrush;
            Brush rowBg = r.IsLarge ? (buy ? _buySoftBrush : _sellSoftBrush) : Brushes.Transparent;
            decimal price = PriceConverter.ToPrice(spec, r.PriceTicks);
            list.Add(new TapeRowVm(
                r.ExchangeTimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                price.ToString("N2", CultureInfo.InvariantCulture),
                r.Quantity.ToString("N0", CultureInfo.InvariantCulture),
                side, sideBrush, rowBg));
        }

        return list;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        await _feed.DisposeAsync();
    }

    private void ApplyViewModel()
    {
        InstrumentTitleText.Text = _viewModel.SelectedInstrument;
        ContractText.Text = "…";
        SessionText.Text = _viewModel.SessionLabel;
        SimulatedBanner.Text = "SIMULATED DATA";
        ReadOnlyBanner.Text = "READ ONLY";
        RithmicStatusText.Text = _viewModel.RithmicStatus;
    }
}
