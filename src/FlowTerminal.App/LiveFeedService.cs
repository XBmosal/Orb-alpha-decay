using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Detectors;
using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Analytics.PriceAction;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Analytics.Vwap;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Charting.Heatmap;
using FlowTerminal.Charting.Overlays;
using FlowTerminal.Charting.Tape;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.Domain.Sessions;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Pipeline;
using FlowTerminal.MarketData.Synthetic;
using FlowTerminal.OrderBook;
using SkiaSharp;

namespace FlowTerminal.App;

public sealed record ChartSnapshot(
    IReadOnlyList<Bar> Bars,
    IReadOnlyList<DomRow> Dom,
    long Cvd,
    IReadOnlyList<TapeRow> Tape,
    bool BookValid,
    string? BookInvalidReason,
    DiagnosticsSnapshot Diagnostics,
    IReadOnlyList<Detection> Detections,
    long TotalDetections,
    ChartOverlays Overlays);

/// <summary>
/// Drives a mock (or, when wired, live) feed through the canonical pipeline and the
/// Phase-3 analytics, exposing coalesced snapshots for the UI. Ingestion and
/// analytics run on the pipeline's single-writer thread; the UI samples the latest
/// snapshot on its own timer, so rendering is decoupled from event processing and
/// canonical events are never dropped to keep up with the screen.
/// </summary>
public sealed class LiveFeedService : IAsyncDisposable
{
    private const int MaxChartBars = 500;

    private readonly object _lock = new();
    private MarketByPriceOrderBook _book = new();
    private VolumeProfile _profile = new();
    private CvdCalculator _cvd = new();
    private TimeAndSalesModel _tape = new();
    private IBarAggregator _bars = BarAggregator.Time(TimeSpan.FromMinutes(1));
    private readonly List<Bar> _completed = new();
    private readonly List<double> _vwapByBar = new();
    private MultiVwap _multiVwap = new();
    private FairValueGapDetector _fvg = new();
    private readonly List<FvgBox> _fvgs = new();
    private readonly TradingCalendar _calendar = new();
    private readonly List<Footprint> _barFootprints = new();
    private Footprint _currentFootprint = new();
    private TpoProfile? _tpo;
    private OpeningRangeBreakout? _orb;
    private int _barsEvicted;
    private long _lastWarmPriceTicks;
    private const int FootprintBars = 20;

    // Warm-start sizing: aim for ~50 bars of recent synthetic history at the current
    // timeframe, but cap the replay span so even high timeframes stay responsive.
    private const int TargetWarmBars = 50;
    private const int MinWarmMinutes = 20;
    private const int MaxWarmMinutes = 480; // ~8h cap keeps even high-TF switches ~2s

    private LiquidityHeatmap _heatmap = new(TimeSpan.FromMilliseconds(250));
    private readonly HeatmapRenderer _heatmapRenderer = new();
    private readonly DetectorEngine _detectors = new(RootSymbol.NQ);
    private readonly PipelineDiagnostics _diagnostics = new();

    private InstrumentPipeline? _pipeline;
    private IMarketDataProvider? _provider;
    private CancellationTokenSource? _cts;
    private Contract? _contract;
    private TimeSpan _interval = TimeSpan.FromMinutes(1);
    private int _switching;

    /// <summary>The bar timeframe the feed is currently aggregating.</summary>
    public TimeSpan Timeframe => _interval;

    public Task StartAsync(Contract contract)
    {
        _contract = contract;
        return StartFeedAsync();
    }

    /// <summary>
    /// Switches the bar timeframe. The current feed is stopped, the per-session
    /// analytics are reset, history is re-warmed at the new interval and the live
    /// stream resumes — detector toggles and diagnostics are preserved. This is a
    /// view/aggregation change only; no orders or trading state are involved.
    /// </summary>
    public async Task ChangeTimeframeAsync(TimeSpan interval)
    {
        if (interval == _interval || _contract is null)
        {
            return;
        }

        // Ignore overlapping switches (rapid clicks) until the in-flight one settles.
        if (Interlocked.Exchange(ref _switching, 1) == 1)
        {
            return;
        }

        try
        {
            await StopFeedAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _interval = interval;
                ResetSessionState();
            }

            await StartFeedAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _switching, 0);
        }
    }

    private async Task StartFeedAsync()
    {
        _cts = new CancellationTokenSource();

        // 1) Warm-start with ~recent synthetic history (off the UI thread) so the
        //    chart, profile, VWAP, footprint and TPO are populated the moment the
        //    window opens, instead of slowly building one live bar at a time.
        const decimal startPrice = 20_000m;
        await Task.Run(() => WarmUp(_contract!, startPrice), _cts.Token).ConfigureAwait(false);

        // 2) Go live, continuing from the warmed-up price so there is no seam.
        decimal liveStart = _lastWarmPriceTicks > 0
            ? PriceConverter.ToPrice(_contract!.Spec, _lastWarmPriceTicks)
            : startPrice;

        _provider = new MockMarketDataProvider(
            1, _contract!, new SyntheticOptions { Seed = 7, StartPrice = liveStart }, realTimePacing: true);
        _pipeline = new InstrumentPipeline(1, ProcessAsync, _diagnostics);
        _pipeline.Start();

        await _provider.ConnectAsync(_cts.Token);
        await _provider.SubscribeAsync(_contract!, SubscriptionOptions.Default, _cts.Token);
        _ = Task.Run(() => PumpAsync(_cts.Token));
    }

    private async Task StopFeedAsync()
    {
        _cts?.Cancel();
        if (_provider is not null)
        {
            await _provider.DisconnectAsync().ConfigureAwait(false);
            await _provider.DisposeAsync().ConfigureAwait(false);
            _provider = null;
        }

        if (_pipeline is not null)
        {
            await _pipeline.DisposeAsync().ConfigureAwait(false);
            _pipeline = null;
        }

        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Clears the per-session market-data and analytics aggregates so history can be
    /// rebuilt at a new timeframe. The detector engine and diagnostics are kept so the
    /// user's study toggles and the running counters survive a timeframe change.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private void ResetSessionState()
    {
        _book = new MarketByPriceOrderBook();
        _profile = new VolumeProfile();
        _cvd = new CvdCalculator();
        _tape = new TimeAndSalesModel();
        _bars = BarAggregator.Time(_interval);
        _completed.Clear();
        _vwapByBar.Clear();
        _multiVwap = new MultiVwap();
        _fvg = new FairValueGapDetector();
        _fvgs.Clear();
        _barFootprints.Clear();
        _currentFootprint = new Footprint();
        _tpo = null;
        _orb = null;
        _barsEvicted = 0;
        _lastWarmPriceTicks = 0;
        _heatmap = new LiquidityHeatmap(TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    /// Synchronously replays a window of synthetic history through the same analytics
    /// the live feed uses, populating the chart before the live stream begins. The
    /// span scales with the timeframe so the chart shows a useful number of bars.
    /// </summary>
    private void WarmUp(Contract contract, decimal startPrice)
    {
        var now = DateTime.UtcNow;
        int warmMinutes = Math.Clamp(
            (int)(_interval.TotalMinutes * TargetWarmBars), MinWarmMinutes, MaxWarmMinutes);
        var warmStart = now - TimeSpan.FromMinutes(warmMinutes);
        var gen = new SyntheticSessionGenerator(1, contract, warmStart,
            new SyntheticOptions { Seed = 7, StartPrice = startPrice });

        for (int guard = 0; guard < 8_000_000; guard++)
        {
            var e = gen.Next();
            if (e.ExchangeTimestampUtc >= now)
            {
                break;
            }

            // Warm-up only builds what the historical chart needs (bars, profile,
            // VWAP, footprint, TPO). The liquidity heatmap and the detector engine are
            // intentionally skipped here: the heatmap's per-event forward-fill and the
            // detectors processing every print are the slow path, and neither produces
            // anything visible on the *historical* candle view. They start live with
            // the real stream. This keeps warm-up well under a second instead of ~40s.
            Ingest(e, warmUp: true);
            if (e.Type == MarketEventType.Trade)
            {
                _lastWarmPriceTicks = e.PriceTicks;
            }
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var ev in _provider!.StreamAsync(ct))
            {
                await _pipeline!.EnqueueAsync(ev, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private ValueTask ProcessAsync(MarketEvent e, CancellationToken ct)
    {
        Ingest(e, warmUp: false);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Processes one canonical event into the analytics. When <paramref name="warmUp"/>
    /// is true the heavy, history-replay-only-irrelevant work (the liquidity heatmap's
    /// per-event forward-fill and the detector engine) is skipped so the historical
    /// chart can be built in a fraction of a second; the live stream passes false so
    /// every engine runs in real time.
    /// </summary>
    private void Ingest(MarketEvent e, bool warmUp)
    {
        lock (_lock)
        {
            _book.Apply(e);
            if (!warmUp)
            {
                _heatmap.OnClock(e.ExchangeTimestampUtc);
                _detectors.OnEvent(e);
                if (e.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
                {
                    _heatmap.OnDepth(e);
                }
            }

            if (e.Type == MarketEventType.Trade)
            {
                _profile.AddTrade(e);
                _cvd.Add(e);
                _tape.Add(e);
                _multiVwap.AddTrade(e.PriceTicks, e.Quantity, _calendar.TradingDate(e.ExchangeTimestampUtc));
                _currentFootprint.AddTrade(e);
                _tpo ??= new TpoProfile(e.ExchangeTimestampUtc);
                _tpo.AddTrade(e.PriceTicks, e.ExchangeTimestampUtc);

                // Opening-range breakout: 5-minute range from the first trade seen.
                _orb ??= new OpeningRangeBreakout(e.ExchangeTimestampUtc.AddMinutes(5));
                _orb.OnTrade(e.PriceTicks, e.ExchangeTimestampUtc);

                if (_bars.AddTrade(e) is { } completed)
                {
                    if (!warmUp) _detectors.OnBar(completed);
                    _completed.Add(completed);
                    _vwapByBar.Add(_multiVwap.Daily.VwapTicks);
                    _barFootprints.Add(_currentFootprint);
                    _currentFootprint = new Footprint();

                    int absoluteIndex = _barsEvicted + _completed.Count - 1;
                    if (_fvg.OnBar(completed) is { } gap)
                    {
                        _fvgs.Add(new FvgBox(absoluteIndex, gap.Direction, gap.TopTicks, gap.BottomTicks));
                        if (_fvgs.Count > 64) _fvgs.RemoveRange(0, _fvgs.Count - 64);
                    }

                    if (_completed.Count > MaxChartBars)
                    {
                        int remove = _completed.Count - MaxChartBars;
                        _completed.RemoveRange(0, remove);
                        _vwapByBar.RemoveRange(0, remove);
                        _barFootprints.RemoveRange(0, remove);
                        _barsEvicted += remove;
                    }
                }
            }
        }
    }

    /// <summary>Latest coalesced snapshot for rendering. Cheap; safe to call at frame rate.</summary>
    public ChartSnapshot Snapshot()
    {
        lock (_lock)
        {
            var bars = new List<Bar>(_completed.Count + 1);
            bars.AddRange(_completed);

            // VWAP series aligned to the bar list (developing bar gets the live value).
            var vwap = new List<double>(_vwapByBar.Count + 1);
            vwap.AddRange(_vwapByBar);
            if (_bars.HasDeveloping)
            {
                bars.Add(_bars.Developing);
                vwap.Add(_multiVwap.Daily.VwapTicks);
            }

            // FVG boxes rebased to the current (post-eviction) bar indexing.
            var fvgs = new List<FvgBox>(_fvgs.Count);
            foreach (var f in _fvgs)
            {
                int rel = f.BarIndex - _barsEvicted;
                if (rel >= 0 && rel < bars.Count)
                {
                    fvgs.Add(f with { BarIndex = rel });
                }
            }

            // Footprint columns for the most recent bars (for the footprint view).
            var footprint = new List<FootprintColumn>();
            int fpStart = Math.Max(0, _completed.Count - FootprintBars);
            for (int i = fpStart; i < _completed.Count; i++)
            {
                var bar = _completed[i];
                var fp = _barFootprints[i];
                var cells = new List<FootprintCell>();
                foreach (var lvl in fp.Levels())
                {
                    cells.Add(new FootprintCell(lvl.PriceTicks, lvl.SellVolume, lvl.BuyVolume));
                }

                footprint.Add(new FootprintColumn(bar.OpenTicks, bar.CloseTicks, bar.HighTicks, bar.LowTicks, cells));
            }

            // TPO rows (lettered brackets per price) over the session.
            var tpoRows = new List<TpoRow>();
            if (_tpo is not null)
            {
                foreach (var lvl in _profile.Levels())
                {
                    string letters = _tpo.LettersAt(lvl.PriceTicks);
                    if (letters.Length > 0) tpoRows.Add(new TpoRow(lvl.PriceTicks, letters));
                }
            }

            var va = _profile.ComputeValueArea();
            var overlays = new ChartOverlays(
                _profile.Levels(), _profile.PocTicks(), va.VahTicks, va.ValTicks,
                vwap, fvgs,
                _orb is { IsEstablished: true } orb ? orb.HighTicks : null,
                _orb is { IsEstablished: true } orb2 ? orb2.LowTicks : null,
                footprint, tpoRows);

            var dom = ReadOnlyDom.Build(_book, _profile, 12);
            return new ChartSnapshot(
                bars, dom, _cvd.CumulativeDelta, _tape.Latest(50),
                _book.IsValid, _book.InvalidReason, _diagnostics.Snapshot(),
                _detectors.Recent(8), _detectors.TotalDetections, overlays);
        }
    }

    /// <summary>
    /// Renders the liquidity heatmap under the feed lock (no copy of session history).
    /// Called from the UI control's paint; the model keeps history tiled so this does
    /// not rebuild the whole session.
    /// </summary>
    /// <summary>Enables/disables a detector by its display name (from the Studies panel).</summary>
    public void SetDetectorEnabled(string detectorName, bool enabled)
    {
        foreach (var d in _detectors.Detectors)
        {
            if (d.Name == detectorName)
            {
                d.Enabled = enabled;
            }
        }
    }

    public void RenderHeatmap(SKCanvas canvas, SKRect bounds)
    {
        lock (_lock)
        {
            long bid = _book.BestBidTicks;
            long ask = _book.BestAskTicks;
            long mid = bid != BookSide.NoPrice && ask != BookSide.NoPrice ? (bid + ask) / 2 : 80_000;
            const long window = 60; // ticks above/below mid
            _heatmapRenderer.Render(canvas, bounds, _heatmap, mid - window, mid + window, HeatmapScale.Percentile);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopFeedAsync().ConfigureAwait(false);
    }
}
