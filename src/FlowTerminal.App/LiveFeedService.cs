using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Detectors;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Charting.Heatmap;
using FlowTerminal.Charting.Tape;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
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
    long TotalDetections);

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
    private readonly MarketByPriceOrderBook _book = new();
    private readonly VolumeProfile _profile = new();
    private readonly CvdCalculator _cvd = new();
    private readonly TimeAndSalesModel _tape = new();
    private readonly IBarAggregator _bars = BarAggregator.Time(TimeSpan.FromMinutes(1));
    private readonly List<Bar> _completed = new();
    private readonly LiquidityHeatmap _heatmap = new(TimeSpan.FromMilliseconds(250));
    private readonly HeatmapRenderer _heatmapRenderer = new();
    private readonly DetectorEngine _detectors = new(RootSymbol.NQ);
    private readonly PipelineDiagnostics _diagnostics = new();

    private InstrumentPipeline? _pipeline;
    private IMarketDataProvider? _provider;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(Contract contract)
    {
        _cts = new CancellationTokenSource();
        // MBP needs depth context; seed a snapshot boundary so the book establishes.
        _provider = new MockMarketDataProvider(1, contract, new SyntheticOptions { Seed = 7 }, realTimePacing: true);
        _pipeline = new InstrumentPipeline(1, ProcessAsync, _diagnostics);
        _pipeline.Start();

        await _provider.ConnectAsync(_cts.Token);
        await _provider.SubscribeAsync(contract, SubscriptionOptions.Default, _cts.Token);
        _ = Task.Run(() => PumpAsync(_cts.Token));
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
        lock (_lock)
        {
            _book.Apply(e);
            _heatmap.OnClock(e.ExchangeTimestampUtc);
            _detectors.OnEvent(e);
            if (e.Type is MarketEventType.BidUpdate or MarketEventType.AskUpdate)
            {
                _heatmap.OnDepth(e);
            }

            if (e.Type == MarketEventType.Trade)
            {
                _profile.AddTrade(e);
                _cvd.Add(e);
                _tape.Add(e);
                if (_bars.AddTrade(e) is { } completed)
                {
                    _detectors.OnBar(completed);
                    _completed.Add(completed);
                    if (_completed.Count > MaxChartBars)
                    {
                        _completed.RemoveRange(0, _completed.Count - MaxChartBars);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Latest coalesced snapshot for rendering. Cheap; safe to call at frame rate.</summary>
    public ChartSnapshot Snapshot()
    {
        lock (_lock)
        {
            var bars = new List<Bar>(_completed.Count + 1);
            bars.AddRange(_completed);
            if (_bars.HasDeveloping)
            {
                bars.Add(_bars.Developing);
            }

            var dom = ReadOnlyDom.Build(_book, _profile, 12);
            return new ChartSnapshot(
                bars, dom, _cvd.CumulativeDelta, _tape.Latest(50),
                _book.IsValid, _book.InvalidReason, _diagnostics.Snapshot(),
                _detectors.Recent(8), _detectors.TotalDetections);
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
        _cts?.Cancel();
        if (_provider is not null)
        {
            await _provider.DisconnectAsync();
            await _provider.DisposeAsync();
        }

        if (_pipeline is not null)
        {
            await _pipeline.DisposeAsync();
        }

        _cts?.Dispose();
    }
}
