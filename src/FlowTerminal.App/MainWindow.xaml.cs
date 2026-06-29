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
    private long _lastEvents;
    private DateTime _lastRateTime = DateTime.UtcNow;
    private readonly TemplateStore _templateStore = new();
    private List<ChartTemplate> _templates = new();
    private readonly Dictionary<string, Action<bool>> _indicatorSetters = new();

    // Cached brushes (resolved once) so the per-frame tape rows don't hit FindResource.
    private Brush _buyBrush = Brushes.LimeGreen;
    private Brush _sellBrush = Brushes.MediumPurple;
    private Brush _neutralBrush = Brushes.Gray;
    private Brush _buySoftBrush = Brushes.Transparent;
    private Brush _sellSoftBrush = Brushes.Transparent;
    private Brush _warningBrush = Brushes.Orange;
    private Brush _secondaryBrush = Brushes.Gray;
    private Brush _mutedBrush = Brushes.Gray;
    private Brush _pocSoftBrush = Brushes.Transparent;
    private Brush _bestRowBrush = Brushes.Transparent;

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
        BuildChartTypeBar();
        BuildTapeFilters();
        BuildCvdModeBar();
        BuildInstrumentMenu();
        BuildDrawingToolbar();
        BuildIndicatorsMenu();

        _templates = _templateStore.Load();
        BuildTemplatesMenu();
        TemplateSaveButton.Click += (_, _) => SaveCurrentTemplate();
        TemplateNameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveCurrentTemplate(); };

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
        _mutedBrush = Brush("TextMutedBrush", _mutedBrush);
        _pocSoftBrush = Brush("WarningSoftBrush", _pocSoftBrush);
        _bestRowBrush = Brush("SurfaceElevatedBrush", _bestRowBrush);
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
        if (sender is ToggleButton { Tag: TimeSpan interval })
        {
            await SelectTimeframeAsync(interval);
        }
    }

    private async Task SelectTimeframeAsync(TimeSpan interval)
    {
        foreach (var b in _timeframeButtons)
        {
            b.IsChecked = b.Tag is TimeSpan t && t == interval;
        }

        UpdateInstrumentTitle();
        await _feed.ChangeTimeframeAsync(interval);
    }

    // ── Drawing toolbar ─────────────────────────────────────────────────────

    private readonly List<ToggleButton> _toolButtons = new();

    private void BuildDrawingToolbar()
    {
        var tools = new (string Glyph, string Tip, Charting.Drawings.DrawingTool Tool)[]
        {
            ("⌖", "Cursor — pan & zoom", Charting.Drawings.DrawingTool.Select),
            ("╱", "Trend line", Charting.Drawings.DrawingTool.Trendline),
            ("─", "Horizontal line", Charting.Drawings.DrawingTool.HorizontalLine),
            ("→", "Ray", Charting.Drawings.DrawingTool.Ray),
            ("▢", "Rectangle", Charting.Drawings.DrawingTool.Rectangle),
            ("F", "Fibonacci retracement", Charting.Drawings.DrawingTool.Fibonacci),
            ("M", "Measure", Charting.Drawings.DrawingTool.Measure),
            ("⌫", "Erase a drawing", Charting.Drawings.DrawingTool.Erase),
        };

        foreach (var (glyph, tip, tool) in tools)
        {
            var btn = new ToggleButton
            {
                Content = glyph,
                Style = (Style)FindResource("IconToggleStyle"),
                Tag = tool,
                ToolTip = tip,
                IsChecked = tool == Charting.Drawings.DrawingTool.Select,
                Margin = new Thickness(0, 0, 0, 3),
            };
            btn.Click += OnToolClick;
            _toolButtons.Add(btn);
            DrawingToolbar.Children.Add(btn);
        }

        // Separator + clear-all.
        DrawingToolbar.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BorderSubtleBrush"),
            Margin = new Thickness(3, 4, 3, 5),
        });

        var clear = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("IconButtonStyle"),
            ToolTip = "Clear all drawings",
            Foreground = _secondaryBrush,
        };
        clear.Click += (_, _) => Chart.ClearDrawings();
        DrawingToolbar.Children.Add(clear);
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not Charting.Drawings.DrawingTool tool) return;
        foreach (var b in _toolButtons) b.IsChecked = ReferenceEquals(b, clicked);
        Chart.ActiveTool = tool;
    }

    // ── Instrument / contract selectors ─────────────────────────────────────

    private readonly ContractCalendar _calendar = new();

    private void BuildInstrumentMenu()
    {
        foreach (var spec in InstrumentRegistry.All)
        {
            var root = spec.Root;
            InstrumentMenu.Children.Add(BuildMenuRow(
                $"{spec.RootSymbol}  —  {spec.Description}",
                current: _contract?.Root == root,
                onClick: async () =>
                {
                    InstrumentButton.IsChecked = false;
                    var active = _calendar.SuggestActive(root, DateOnly.FromDateTime(DateTime.UtcNow));
                    await SwitchContractAsync(active);
                }));
        }
    }

    private void BuildContractMenu()
    {
        ContractMenu.Children.Clear();
        if (_contract is null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var contracts = _calendar.Enumerate(_contract.Root, today.Year, today.Year + 1)
            .Where(c => c.ExpirationDateUtc >= today)
            .Take(5);

        foreach (var c in contracts)
        {
            var pick = c;
            ContractMenu.Children.Add(BuildMenuRow(
                FriendlyContract(c),
                current: c.FullSymbol == _contract.FullSymbol,
                onClick: async () =>
                {
                    ContractButton.IsChecked = false;
                    await SwitchContractAsync(pick);
                }));
        }
    }

    private FrameworkElement BuildMenuRow(string text, bool current, Func<Task> onClick)
    {
        var dock = new DockPanel { LastChildFill = true };
        var check = new TextBlock
        {
            Text = current ? "✓" : string.Empty,
            Foreground = _buyBrush,
            FontWeight = FontWeights.Bold,
            Width = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(check, Dock.Right);
        dock.Children.Add(check);
        dock.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = _secondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var row = new Border
        {
            Padding = new Thickness(10, 6, 8, 6),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = dock,
        };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += async (_, _) => await onClick();
        return row;
    }

    private static string FriendlyContract(Contract c)
    {
        string month = new DateTime(c.Year, c.Month.CalendarMonth(), 1)
            .ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        return $"{c.Spec.RootSymbol} {month} {c.Year}";
    }

    private async Task SwitchContractAsync(Contract contract)
    {
        _contract = contract;
        Chart.TickSize = contract.Spec.TickSize;
        InstrumentTitleText.Text = $"{contract.Spec.Description} Futures";
        ContractText.Text = FriendlyContract(contract);
        UpdateInstrumentTitle();
        BuildContractMenu();
        await _feed.ChangeContractAsync(contract);
    }

    // ── Chart type selector ─────────────────────────────────────────────────

    private static readonly (string Label, Charting.ChartType Type)[] ChartTypes =
    {
        ("Candles", Charting.ChartType.Candles),
        ("Bars", Charting.ChartType.Bars),
        ("Line", Charting.ChartType.Line),
    };

    private readonly List<ToggleButton> _chartTypeButtons = new();

    private void BuildChartTypeBar()
    {
        foreach (var (label, type) in ChartTypes)
        {
            var btn = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("SegmentedToggleStyle"),
                Tag = type,
                IsChecked = type == Chart.ChartType,
            };

            btn.Click += OnChartTypeClick;
            _chartTypeButtons.Add(btn);
            ChartTypeBar.Children.Add(btn);
        }
    }

    private void OnChartTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: Charting.ChartType type })
        {
            SelectChartType(type);
        }
    }

    private void SelectChartType(Charting.ChartType type)
    {
        foreach (var b in _chartTypeButtons)
        {
            b.IsChecked = b.Tag is Charting.ChartType t && t == type;
        }

        Chart.ChartType = type;
    }

    // ── CVD display mode ────────────────────────────────────────────────────

    private enum CvdView { Number, Line, Candles }

    private CvdView _cvdView = CvdView.Line;
    private readonly List<ToggleButton> _cvdModeButtons = new();

    private void BuildCvdModeBar()
    {
        foreach (var (label, val) in new[] { ("Num", CvdView.Number), ("Line", CvdView.Line), ("Candle", CvdView.Candles) })
        {
            var btn = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("SegmentedToggleStyle"),
                Tag = val,
                IsChecked = val == CvdView.Line,
                MinWidth = 30,
            };
            btn.Click += OnCvdModeClick;
            _cvdModeButtons.Add(btn);
            CvdModeBar.Children.Add(btn);
        }

        ApplyCvdView(CvdView.Line);
    }

    private void OnCvdModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: CvdView val }) SelectCvdView(val);
    }

    private void SelectCvdView(CvdView view)
    {
        foreach (var b in _cvdModeButtons) b.IsChecked = b.Tag is CvdView v && v == view;
        ApplyCvdView(view);
    }

    private void ApplyCvdView(CvdView view)
    {
        _cvdView = view;
        CvdNumberView.Visibility = view == CvdView.Number ? Visibility.Visible : Visibility.Collapsed;
        CvdChartView.Visibility = view == CvdView.Number ? Visibility.Collapsed : Visibility.Visible;
        if (view == CvdView.Candles) CvdChart.DisplayMode = Controls.CvdHost.Mode.Candles;
        else if (view == CvdView.Line) CvdChart.DisplayMode = Controls.CvdHost.Mode.Line;
        CvdChart.InvalidateVisual();
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
            void SetState(bool v)
            {
                check.Text = v ? "✓" : string.Empty;
                Apply(study, v);
            }

            row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceHoverBrush");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => SetState(check.Text.Length == 0);
            _indicatorSetters[study.ShortCode] = SetState;
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

    // ── Templates (saved chart layouts) ─────────────────────────────────────

    private void BuildTemplatesMenu()
    {
        TemplatesListPanel.Children.Clear();
        if (_templates.Count == 0)
        {
            TemplatesListPanel.Children.Add(new TextBlock
            {
                Text = "No saved templates yet.",
                Foreground = _mutedBrush,
                FontSize = 12,
                Margin = new Thickness(10, 6, 10, 8),
            });
            return;
        }

        foreach (var t in _templates)
        {
            TemplatesListPanel.Children.Add(BuildTemplateRow(t));
        }
    }

    private FrameworkElement BuildTemplateRow(ChartTemplate t)
    {
        var del = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 22,
            Height = 22,
            FontSize = 11,
            Foreground = _mutedBrush,
            ToolTip = "Delete template",
        };
        del.Click += (_, _) =>
        {
            _templates.Remove(t);
            _templateStore.Save(_templates);
            BuildTemplatesMenu();
        };
        DockPanel.SetDock(del, Dock.Right);

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(del);
        dock.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = t.Name, Foreground = _secondaryBrush, FontWeight = FontWeights.SemiBold },
                new TextBlock
                {
                    Text = $"{t.TimeframeMinutes}m · {t.ChartType} · {t.Indicators.Length} indicators",
                    Foreground = _mutedBrush, FontSize = 10.5,
                },
            },
        });

        var row = new Border
        {
            Padding = new Thickness(10, 6, 6, 6),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = dock,
        };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += async (_, _) =>
        {
            TemplatesButton.IsChecked = false;
            await ApplyTemplateAsync(t);
        };
        return row;
    }

    private void SaveCurrentTemplate()
    {
        string name = TemplateNameBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0) return;

        _templates.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        _templates.Add(new ChartTemplate(
            name,
            (int)_feed.Timeframe.TotalMinutes,
            Chart.ChartType.ToString(),
            _cvdView.ToString(),
            _studyState.Enabled.ToArray()));
        _templateStore.Save(_templates);
        TemplateNameBox.Text = string.Empty;
        BuildTemplatesMenu();
    }

    private async Task ApplyTemplateAsync(ChartTemplate t)
    {
        if (Enum.TryParse<Charting.ChartType>(t.ChartType, out var ct)) SelectChartType(ct);
        if (Enum.TryParse<CvdView>(t.CvdView, out var cv)) SelectCvdView(cv);

        var enabled = new HashSet<string>(t.Indicators);
        foreach (var kv in _indicatorSetters) kv.Value(enabled.Contains(kv.Key));

        await SelectTimeframeAsync(TimeSpan.FromMinutes(Math.Max(1, t.TimeframeMinutes)));
    }

    // ── Lifecycle / render ──────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contract = _calendar.SuggestActive(RootSymbol.NQ, DateOnly.FromDateTime(DateTime.UtcNow));
        ContractText.Text = FriendlyContract(_contract);
        Chart.TickSize = _contract.Spec.TickSize;
        UpdateInstrumentTitle();
        BuildContractMenu();
        _lastRateTime = DateTime.UtcNow;
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
        UpdateDom(snapshot);

        long cvd = snapshot.Cvd;
        string cvdStr = cvd.ToString("N0", CultureInfo.InvariantCulture);
        var cvdBrush = cvd >= 0 ? _buyBrush : _sellBrush;
        CvdText.Text = cvdStr;
        CvdText.Foreground = cvdBrush;
        CvdValueSmall.Text = cvdStr;
        CvdValueSmall.Foreground = cvdBrush;
        CvdChart.Update(snapshot.CvdSeries);

        UpdateBookState(snapshot.BookValid, snapshot.BookInvalidReason);
        TapeList.ItemsSource = BuildTapeRows(snapshot.Tape);
        UpdateOhlc(snapshot.Bars);
        Heatmap.InvalidateVisual(); // repaint the heatmap from the feed's tiled history

        UpdateDiagnostics(snapshot);
    }

    private void UpdateDiagnostics(ChartSnapshot snapshot)
    {
        var d = snapshot.Diagnostics;

        // Events/sec over a ~1/2-second window so the value is steady, not jittery.
        var now = DateTime.UtcNow;
        double elapsed = (now - _lastRateTime).TotalSeconds;
        if (elapsed >= 0.5)
        {
            long delta = Math.Max(0, d.EventsProcessed - _lastEvents);
            MetricEventsRate.Text = ((long)(delta / elapsed)).ToString("N0", CultureInfo.InvariantCulture);
            _lastEvents = d.EventsProcessed;
            _lastRateTime = now;
        }

        MetricQueue.Text = d.CurrentQueueDepth.ToString("N0", CultureInfo.InvariantCulture);
        MetricDropped.Text = d.DroppedCanonicalEvents.ToString("N0", CultureInfo.InvariantCulture);
        MetricDropped.Foreground = d.DroppedCanonicalEvents > 0 ? _warningBrush : _secondaryBrush;
        MetricGaps.Text = d.SequenceGaps.ToString("N0", CultureInfo.InvariantCulture);
        MetricGaps.Foreground = d.SequenceGaps > 0 ? _warningBrush : _secondaryBrush;
        MetricSignals.Text = snapshot.TotalDetections.ToString("N0", CultureInfo.InvariantCulture);
        MetricLastSignal.Text = snapshot.Detections.Count > 0 ? snapshot.Detections[0].Label : "—";
    }

    private void UpdateBookState(bool valid, string? reason)
    {
        if (valid)
        {
            BookStateDot.Fill = _buyBrush;
            BookStateText.Text = "Book Valid";
            BookStateText.Foreground = _secondaryBrush;
            BookStateChip.ToolTip = null;
            MetricBookDot.Fill = _buyBrush;
            MetricBookState.Text = "Valid";
            MetricBookState.Foreground = _secondaryBrush;
        }
        else
        {
            BookStateDot.Fill = _warningBrush;
            BookStateText.Text = "Book Invalid";
            BookStateText.Foreground = _warningBrush;
            BookStateChip.ToolTip = reason is null ? "Order book is temporarily invalid." : $"Order book invalid: {reason}";
            MetricBookDot.Fill = _warningBrush;
            MetricBookState.Text = "Invalid";
            MetricBookState.Foreground = _warningBrush;
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

    /// <summary>Row view-model for the read-only DOM ladder (display-only fields).</summary>
    public sealed record DomRowVm(
        string Price, string Bid, string Ask, string CumBid, string CumAsk,
        GridLength BidFill, GridLength BidRest, GridLength AskFill, GridLength AskRest,
        Brush RowBackground, Brush PriceBrush, Brush BidBrush, Brush AskBrush);

    private void UpdateDom(ChartSnapshot snapshot)
    {
        var rows = snapshot.Dom;
        if (_contract is null || rows.Count == 0)
        {
            DomList.ItemsSource = null;
            BidPriceText.Text = "—";
            AskPriceText.Text = "—";
            SpreadText.Text = "—";
            return;
        }

        var spec = _contract.Spec;
        long bestBid = snapshot.BestBidTicks;
        long bestAsk = snapshot.BestAskTicks;

        long maxSize = 1;
        foreach (var r in rows) maxSize = Math.Max(maxSize, Math.Max(r.BidSize, r.AskSize));

        var list = new List<DomRowVm>(rows.Count);
        foreach (var r in rows)
        {
            bool bidSide = r.PriceTicks <= bestBid;
            bool askSide = r.PriceTicks >= bestAsk;
            bool isBest = r.PriceTicks == bestBid || r.PriceTicks == bestAsk;

            double bidFrac = bidSide ? Math.Clamp(r.BidSize / (double)maxSize, 0, 1) : 0;
            double askFrac = askSide ? Math.Clamp(r.AskSize / (double)maxSize, 0, 1) : 0;

            Brush rowBg = r.IsPoc ? _pocSoftBrush : isBest ? _bestRowBrush : Brushes.Transparent;
            Brush priceBrush = r.PriceTicks == bestAsk ? _sellBrush
                : r.PriceTicks == bestBid ? _buyBrush : _secondaryBrush;

            list.Add(new DomRowVm(
                PriceConverter.ToPrice(spec, r.PriceTicks).ToString("N2", CultureInfo.InvariantCulture),
                bidSide && r.BidSize > 0 ? r.BidSize.ToString("N0", CultureInfo.InvariantCulture) : string.Empty,
                askSide && r.AskSize > 0 ? r.AskSize.ToString("N0", CultureInfo.InvariantCulture) : string.Empty,
                bidSide && r.CumulativeBid > 0 ? r.CumulativeBid.ToString("N0", CultureInfo.InvariantCulture) : string.Empty,
                askSide && r.CumulativeAsk > 0 ? r.CumulativeAsk.ToString("N0", CultureInfo.InvariantCulture) : string.Empty,
                new GridLength(bidFrac, GridUnitType.Star), new GridLength(1 - bidFrac, GridUnitType.Star),
                new GridLength(askFrac, GridUnitType.Star), new GridLength(1 - askFrac, GridUnitType.Star),
                rowBg, priceBrush, _buyBrush, _sellBrush));
        }

        DomList.ItemsSource = list;

        if (snapshot.BookValid)
        {
            BidPriceText.Text = PriceConverter.ToPrice(spec, bestBid).ToString("N2", CultureInfo.InvariantCulture);
            AskPriceText.Text = PriceConverter.ToPrice(spec, bestAsk).ToString("N2", CultureInfo.InvariantCulture);
            decimal spread = PriceConverter.ToPrice(spec, bestAsk) - PriceConverter.ToPrice(spec, bestBid);
            SpreadText.Text = spread.ToString("N2", CultureInfo.InvariantCulture);
        }
    }

    // ── Time & Sales filters ────────────────────────────────────────────────

    private enum SideFilter { All, Buy, Sell }

    private SideFilter _tapeSide = SideFilter.All;
    private static readonly long[] MinSizes = { 0, 10, 25, 50, 100, 250 };
    private int _minSizeIdx;
    private readonly List<ToggleButton> _sideFilterButtons = new();

    private void BuildTapeFilters()
    {
        foreach (var (label, val) in new[] { ("All", SideFilter.All), ("Buy", SideFilter.Buy), ("Sell", SideFilter.Sell) })
        {
            var btn = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("SegmentedToggleStyle"),
                Tag = val,
                IsChecked = val == _tapeSide,
                MinWidth = 30,
            };
            btn.Click += OnSideFilterClick;
            _sideFilterButtons.Add(btn);
            SideFilterBar.Children.Add(btn);
        }

        MinSizeButton.Click += (_, _) =>
        {
            _minSizeIdx = (_minSizeIdx + 1) % MinSizes.Length;
            long m = MinSizes[_minSizeIdx];
            MinSizeButton.Content = m == 0 ? "≥ 0" : $"≥ {m}";
        };
    }

    private void OnSideFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not SideFilter val) return;
        _tapeSide = val;
        foreach (var b in _sideFilterButtons) b.IsChecked = ReferenceEquals(b, clicked);
    }

    /// <summary>Row view-model for the Time &amp; Sales feed (display-only fields).</summary>
    public sealed record TapeRowVm(string Time, string Price, string Size, string Side, Brush SideBrush, Brush RowBackground);

    private List<TapeRowVm> BuildTapeRows(IReadOnlyList<Charting.Tape.TapeRow> rows)
    {
        var spec = _contract!.Spec;
        long minSize = MinSizes[_minSizeIdx];
        var list = new List<TapeRowVm>(Math.Min(30, rows.Count));
        for (int i = 0; i < rows.Count && list.Count < 30; i++)
        {
            var r = rows[i];
            bool buy = r.Aggressor == Domain.Events.AggressorSide.Buy;
            bool sell = r.Aggressor == Domain.Events.AggressorSide.Sell;

            // Apply the active filters (size and aggressor side).
            if (r.Quantity < minSize) continue;
            if (_tapeSide == SideFilter.Buy && !buy) continue;
            if (_tapeSide == SideFilter.Sell && !sell) continue;

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
        RithmicStatusText.Text = "Rithmic (Mock)";
        RithmicStatusText.ToolTip = _viewModel.RithmicStatus;
    }
}
