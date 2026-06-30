using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting.Dom;
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
    // The initialisers are on-identity fallbacks (used only if a theme lookup fails):
    // bullish green / bearish light purple / amber warning, matching Themes/Colors.xaml.
    private static SolidColorBrush Identity(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private Brush _buyBrush = Identity(0x22, 0xC5, 0x5E);   // green
    private Brush _sellBrush = Identity(0xC4, 0xA7, 0xFF);  // light purple
    private Brush _neutralBrush = Identity(0x70, 0x79, 0x89);
    private Brush _buySoftBrush = Brushes.Transparent;
    private Brush _sellSoftBrush = Brushes.Transparent;
    private Brush _warningBrush = Identity(0xF5, 0xB9, 0x42); // amber
    private Brush _secondaryBrush = Identity(0xA8, 0xB0, 0xBF);
    private Brush _mutedBrush = Identity(0x70, 0x79, 0x89);
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
        BuildWorkspaceTabs();
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

        WirePlaybackAndEditing();

        ContrastSlider.ValueChanged += (_, e) =>
        {
            long min = (long)Math.Round(e.NewValue);
            ContrastValueText.Text = $"≥ {min}";
            _feed.SetHeatmapMinSize(min);
        };

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
        ApplyFootprintPreset(_footprintPreset); // re-apply for the new instrument's thresholds
    }

    // ── Workspace tabs (Chart / Heatmap) ────────────────────────────────────

    private readonly List<ToggleButton> _workspaceTabs = new();

    private void BuildWorkspaceTabs()
    {
        foreach (var (label, isChart) in new[] { ("Chart", true), ("Heatmap", false) })
        {
            var btn = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("SegmentedToggleStyle"),
                Tag = isChart,
                IsChecked = isChart,
                MinWidth = 78,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = isChart ? "Chart workspace (C)" : "Heatmap workspace (H)",
            };
            btn.Click += OnWorkspaceTabClick;
            _workspaceTabs.Add(btn);
            WorkspaceTabs.Children.Add(btn);
        }
    }

    private void OnWorkspaceTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not bool isChart) return;
        foreach (var b in _workspaceTabs) b.IsChecked = ReferenceEquals(b, clicked);
        ChartWorkspace.Visibility = isChart ? Visibility.Visible : Visibility.Collapsed;
        HeatmapWorkspace.Visibility = isChart ? Visibility.Collapsed : Visibility.Visible;
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

    // ── Footprint visual presets ────────────────────────────────────────────

    private string _footprintPreset = FlowTerminal.Analytics.Footprints.FootprintPresetRegistry.Default.Name;

    // ── Read-only DOM column layout (show / hide / reorder / resize) ──────────
    //
    // The Skia ladder renders whatever columns the editable layout resolves to. A preset
    // seeds the layout; the user can then customise it. All of this is presentational —
    // there are no order-entry surfaces anywhere in the DOM editor.

    private string _domPreset = "Full Professional";
    private DomLayout _domLayout = DomLayout.FromPreset(DomPresetRegistry.ByName("Full Professional")!);
    private bool _domCustom;                 // true once the layout diverges from its preset
    private const double DomWidthStep = 8;

    // Cached last frame so editing the layout repaints the ladder immediately, even when idle.
    private IReadOnlyList<DomRow> _lastDomRows = Array.Empty<DomRow>();
    private InstrumentSpec? _lastDomSpec;
    private decimal _lastDomTick = 0.25m;

    /// <summary>Replaces the layout with a preset's columns and refreshes the editor + ladder.</summary>
    private void ApplyDomPreset(string name)
    {
        var preset = DomPresetRegistry.ByName(name) ?? DomPresetRegistry.Default;
        _domPreset = preset.Name;
        _domLayout = DomLayout.FromPreset(preset);
        _domCustom = false;
        RefreshDomEditor();
    }

    /// <summary>Restores the serialised custom layout from a template (falls back to preset).</summary>
    private void ApplyDomLayout(string preset, string? layout)
    {
        var basePreset = DomPresetRegistry.ByName(preset) ?? DomPresetRegistry.Default;
        _domPreset = basePreset.Name;
        var restored = DomLayout.Deserialize(layout);
        _domLayout = restored ?? DomLayout.FromPreset(basePreset);
        _domCustom = restored is not null;
        RefreshDomEditor();
    }

    /// <summary>Rebuilds the preset chips, the column list, the button label, and repaints.</summary>
    private void RefreshDomEditor()
    {
        DomPresetButtonText.Text = _domCustom ? $"DOM: {_domPreset}*" : $"DOM: {_domPreset}";
        BuildDomPresetChips();
        BuildDomColumnList();
        RefreshDomLadder();
    }

    /// <summary>Pushes the current layout to the ladder using the last frame's rows.</summary>
    private void RefreshDomLadder() =>
        DomLadder.Update(_lastDomRows, _domLayout.ResolveColumns(), _domLayout.ResolveWidths(), _lastDomSpec, _lastDomTick);

    private void BuildDomPresetChips()
    {
        DomPresetChips.Children.Clear();
        foreach (var preset in DomPresetRegistry.BuiltIns)
        {
            bool active = !_domCustom && preset.Name == _domPreset;
            var chip = new Border
            {
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 5, 5),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource(active ? "AccentBrush" : "BorderSubtleBrush"),
                Background = active ? (Brush)FindResource("SurfaceHoverBrush") : Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = preset.Description + (preset.RequiresMbo ? "  (needs MBO data)" : ""),
                Child = new TextBlock
                {
                    Text = preset.Name,
                    FontSize = 11,
                    Foreground = (Brush)FindResource(active ? "TextBrush" : "TextSecondaryBrush"),
                },
            };
            chip.MouseLeftButtonUp += (_, _) => ApplyDomPreset(preset.Name);
            DomPresetChips.Children.Add(chip);
        }
    }

    private void BuildDomColumnList()
    {
        DomColumnsPanel.Children.Clear();
        var cols = _domLayout.Columns;
        for (int i = 0; i < cols.Count; i++)
            DomColumnsPanel.Children.Add(BuildDomColumnRow(cols[i], i, cols.Count));
    }

    private FrameworkElement BuildDomColumnRow(DomLayoutColumn column, int index, int count)
    {
        var d = column.Descriptor;

        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 1, 2, 1) };

        // Visibility toggle (a small check box drawn as a glyph, matching the indicator menu).
        var check = new TextBlock
        {
            Text = column.Visible ? "✓" : string.Empty,
            Foreground = (Brush)FindResource(d.Side == DomColumnSide.Ask ? "BearishBrush" : "BullishBrush"),
            FontWeight = FontWeights.Bold,
            Width = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(check, Dock.Left);
        dock.Children.Add(check);

        // Reorder + resize controls live on the right.
        var up = DomMiniButton("▲", index > 0, () => { if (_domLayout.MoveUp(index)) MarkDomCustom(); });
        var down = DomMiniButton("▼", index < count - 1, () => { if (_domLayout.MoveDown(index)) MarkDomCustom(); });
        var widthText = new TextBlock
        {
            Text = ((int)Math.Round(column.Width)).ToString(CultureInfo.InvariantCulture),
            FontSize = 10,
            Width = 24,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("MutedTextBrush"),
        };
        var minus = DomMiniButton("−", column.Width > DomLayout.MinWidth,
            () => { _domLayout.SetWidth(column.Type, column.Width - DomWidthStep); MarkDomCustom(); });
        var plus = DomMiniButton("＋", column.Width < DomLayout.MaxWidth,
            () => { _domLayout.SetWidth(column.Type, column.Width + DomWidthStep); MarkDomCustom(); });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(minus);
        controls.Children.Add(widthText);
        controls.Children.Add(plus);
        controls.Children.Add(new Border { Width = 6 });
        controls.Children.Add(up);
        controls.Children.Add(down);
        DockPanel.SetDock(controls, Dock.Right);
        dock.Children.Add(controls);

        // Estimated / MBO capability tag.
        string? tag = d.Requirement.HasFlag(DomDataRequirement.Mbo) ? "MBO"
            : d.Estimated ? "est" : null;
        if (tag is not null)
        {
            var tagBlock = new TextBlock
            {
                Text = tag,
                FontSize = 9,
                Foreground = (Brush)FindResource("MutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
            };
            DockPanel.SetDock(tagBlock, Dock.Right);
            dock.Children.Add(tagBlock);
        }

        var name = new TextBlock
        {
            Text = d.FullName,
            Foreground = (Brush)FindResource(column.Visible ? "TextBrush" : "MutedTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0),
        };
        dock.Children.Add(name);

        var row = new Border
        {
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = dock,
            ToolTip = d.FullName,
        };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        // Clicking the row (outside the buttons) toggles visibility.
        row.MouseLeftButtonUp += (_, _) =>
        {
            _domLayout.SetVisible(column.Type, !column.Visible);
            MarkDomCustom();
        };
        return row;
    }

    private Border DomMiniButton(string glyph, bool enabled, Action onClick)
    {
        var b = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(1, 0, 1, 0),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource(enabled ? "TextSecondaryBrush" : "TextMutedBrush"),
            },
        };
        if (enabled)
        {
            b.MouseEnter += (_, _) => b.Background = (Brush)FindResource("SurfacePrimaryBrush");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        }
        return b;
    }

    /// <summary>Records that the layout was hand-edited, then refreshes the editor + ladder.</summary>
    private void MarkDomCustom()
    {
        _domCustom = true;
        RefreshDomEditor();
    }

    /// <summary>Applies a named footprint preset for the current instrument, preserving the choice.</summary>
    private void ApplyFootprintPreset(string name)
    {
        var preset = FlowTerminal.Analytics.Footprints.FootprintPresetRegistry.ByName(name)
            ?? FlowTerminal.Analytics.Footprints.FootprintPresetRegistry.Default;
        _footprintPreset = preset.Name;
        var root = _contract?.Root ?? RootSymbol.NQ;
        _feed.SetFootprintSettings(preset.ForInstrument(root));
        FootprintModeButton.Content = $"FP: {preset.Name}";
    }

    private void CycleFootprintPreset()
    {
        var presets = FlowTerminal.Analytics.Footprints.FootprintPresetRegistry.BuiltIns;
        int idx = 0;
        for (int i = 0; i < presets.Count; i++) if (presets[i].Name == _footprintPreset) idx = i;
        ApplyFootprintPreset(presets[(idx + 1) % presets.Count].Name);
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

        // The Large/Block Trade study also gates the Big Trade bubbles on the heatmap.
        if (study.ShortCode == "LT")
        {
            _feed.ShowBigTrades = on;
        }
    }

    // ── Undo/redo, add-indicator, and playback transport ────────────────────

    private static readonly double[] Speeds = { 1, 2, 4, 8 };
    private int _speedIdx;

    private void WirePlaybackAndEditing()
    {
        UndoButton.Click += (_, _) => Chart.Undo();
        RedoButton.Click += (_, _) => Chart.Redo();
        AddIndicatorButton.Click += (_, _) => IndicatorsButton.IsChecked = true;
        FootprintModeButton.Click += (_, _) => CycleFootprintPreset();
        // The DOM column editor opens via the toggle button's IsChecked → Popup binding.
        DomFreezeButton.Checked += (_, _) => DomLadder.Frozen = true;
        DomFreezeButton.Unchecked += (_, _) => DomLadder.Frozen = false;

        // Surface keyboard shortcuts in the tooltips of the controls they drive.
        UndoButton.ToolTip = "Undo drawing (Ctrl+Z)";
        RedoButton.ToolTip = "Redo drawing (Ctrl+Y)";
        AddIndicatorButton.ToolTip = "Indicators (I)";
        IndicatorsButton.ToolTip = "Indicators (I)";
        RestartButton.ToolTip = "Restart session";

        RestartButton.Click += async (_, _) => await _feed.RestartAsync();

        PlayPauseButton.Click += (_, _) => ApplyPlayPause();

        SpeedButton.Click += (_, _) =>
        {
            _speedIdx = (_speedIdx + 1) % Speeds.Length;
            double s = Speeds[_speedIdx];
            _feed.SetSpeed(s);
            SpeedButton.Content = $"{s:0}×";
        };

        PreviewKeyDown += OnGlobalKeyDown;
    }

    private void ApplyPlayPause()
    {
        bool paused = PlayPauseButton.IsChecked == true;
        _feed.SetPaused(paused);
        PlayPauseButton.Content = paused ? "▶" : "❚❚";
        PlayPauseButton.ToolTip = paused ? "Resume (Space)" : "Pause (Space)";
    }

    /// <summary>Switches the workspace tab (true = Chart, false = Heatmap).</summary>
    private void SelectWorkspace(bool isChart)
    {
        foreach (var b in _workspaceTabs) b.IsChecked = b.Tag is bool t && t == isChart;
        ChartWorkspace.Visibility = isChart ? Visibility.Visible : Visibility.Collapsed;
        HeatmapWorkspace.Visibility = isChart ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Toggles a study by short code through the same setter the menu uses.</summary>
    private void ToggleStudy(string code)
    {
        if (_indicatorSetters.TryGetValue(code, out var set))
        {
            set(!_studyState.IsEnabled(code));
        }
    }

    private void CloseAllFlyouts()
    {
        IndicatorsButton.IsChecked = false;
        TemplatesButton.IsChecked = false;
        InstrumentButton.IsChecked = false;
        ContractButton.IsChecked = false;
    }

    /// <summary>
    /// Global keyboard shortcuts. Single-key shortcuts are suppressed while typing in a
    /// text box so they never clobber text entry; Ctrl combos always apply. All actions
    /// route to existing, wired controls — nothing here is decorative.
    /// </summary>
    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl)
        {
            if (e.Key == Key.Z) { Chart.Undo(); e.Handled = true; }
            else if (e.Key == Key.Y) { Chart.Redo(); e.Handled = true; }
            return;
        }

        // Don't hijack typing.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F: Chart.ReturnToLive(); e.Handled = true; break;
            case Key.R: Chart.ResetView(); e.Handled = true; break;
            case Key.C: SelectWorkspace(true); e.Handled = true; break;
            case Key.H: SelectWorkspace(false); e.Handled = true; break;
            case Key.P: ToggleStudy("FP"); e.Handled = true; break;
            case Key.I: IndicatorsButton.IsChecked = true; e.Handled = true; break;
            case Key.Space:
                PlayPauseButton.IsChecked = !(PlayPauseButton.IsChecked == true);
                ApplyPlayPause();
                e.Handled = true;
                break;
            case Key.Escape: CloseAllFlyouts(); e.Handled = true; break;
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
            _studyState.Enabled.ToArray())
        {
            FootprintMode = _footprintPreset,
            DomPreset = _domPreset,
            DomLayout = _domCustom ? _domLayout.Serialize() : null,
        });
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

        if (FlowTerminal.Analytics.Footprints.FootprintPresetRegistry.ByName(t.FootprintMode) is not null)
            ApplyFootprintPreset(t.FootprintMode);
        if (DomPresetRegistry.ByName(t.DomPreset) is not null)
            ApplyDomLayout(t.DomPreset, t.DomLayout);

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
        ApplyFootprintPreset(_footprintPreset);
        ApplyDomPreset(_domPreset);
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
        Chart.FootprintSettings = _feed.FootprintSettings;
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

        UpdateSyntheticRealism();
    }

    /// <summary>
    /// Surfaces the synthetic order-book realism diagnostics as a hover tooltip on the
    /// diagnostics chips (a debug-only affordance; it adds no visible styling). Shows
    /// the live regime, spread, level distribution and executed/trade totals so the
    /// mock market's realism can be inspected without affecting the rendered view.
    /// </summary>
    private void UpdateSyntheticRealism()
    {
        if (_feed.SyntheticDiagnostics is not { } d)
        {
            return;
        }

        DiagnosticsChips.ToolTip =
            $"Synthetic book · {d.Regime}\n" +
            $"Spread: {d.SpreadTicks} tick(s)   Levels: {d.BidLevels} bid / {d.AskLevels} ask\n" +
            $"Level size — median {d.MedianLevelSize:N0}, max {d.MaxLevelSize:N0}, walls {d.WallCount}\n" +
            $"Executed: {d.TotalExecuted:N0} over {d.TradeCount:N0} trades   Events: {d.EventCount:N0}";
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

    private void UpdateDom(ChartSnapshot snapshot)
    {
        var rows = snapshot.Dom;
        var spec = _contract?.Spec;
        _lastDomRows = rows;
        _lastDomSpec = spec;
        _lastDomTick = spec?.TickSize ?? 0.25m;
        DomLadder.Update(rows, _domLayout.ResolveColumns(), _domLayout.ResolveWidths(), spec, _lastDomTick);

        if (_contract is null || rows.Count == 0 || !snapshot.BookValid)
        {
            BidPriceText.Text = "—";
            AskPriceText.Text = "—";
            SpreadText.Text = "—";
            return;
        }

        long bestBid = snapshot.BestBidTicks;
        long bestAsk = snapshot.BestAskTicks;
        BidPriceText.Text = PriceConverter.ToPrice(spec!, bestBid).ToString("N2", CultureInfo.InvariantCulture);
        AskPriceText.Text = PriceConverter.ToPrice(spec!, bestAsk).ToString("N2", CultureInfo.InvariantCulture);
        decimal spread = PriceConverter.ToPrice(spec!, bestAsk) - PriceConverter.ToPrice(spec!, bestBid);
        SpreadText.Text = spread.ToString("N2", CultureInfo.InvariantCulture);
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
