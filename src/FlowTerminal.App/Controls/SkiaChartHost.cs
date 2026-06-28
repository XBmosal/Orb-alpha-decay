using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// A single SkiaSharp-backed candlestick chart control with study overlays. The
/// entire chart is drawn on one canvas — there is NO WPF control per candle. The
/// latest bar/overlay snapshot is supplied from the (off-thread) feed and the
/// control repaints on a coalesced timer, so market processing is never tied to the
/// visual frame rate. Which overlays draw is controlled by the shared StudyState.
/// </summary>
public sealed class SkiaChartHost : SKElement
{
    private readonly CandlestickRenderer _renderer = new();
    private readonly ChartOverlayRenderer _overlays = new();
    private readonly FootprintRenderer _footprint = new();
    private IReadOnlyList<Bar> _bars = Array.Empty<Bar>();
    private ChartOverlays _overlayData = ChartOverlays.Empty;
    private StudyState? _studies;
    private readonly int _maxVisibleBars = 120;

    public void Attach(StudyState studies) => _studies = studies;

    public void UpdateFrame(IReadOnlyList<Bar> bars, ChartOverlays overlays)
    {
        _bars = bars;
        _overlayData = overlays;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width;
        float h = e.Info.Height;

        if (_bars.Count == 0 || w <= 0 || h <= 0)
        {
            canvas.Clear(_renderer.Palette.Background.ToSkColor());
            return;
        }

        // Footprint mode replaces the candle view with wide bid×ask columns.
        if (_studies is not null && _studies.IsEnabled("FP") && _overlayData.Footprint.Count > 0)
        {
            _footprint.Render(canvas, new SKRect(0, 0, w, h), _overlayData.Footprint);
            return;
        }

        int visible = Math.Min(_maxVisibleBars, _bars.Count);
        int first = _bars.Count - visible;

        long min = long.MaxValue, max = long.MinValue;
        for (int i = first; i < _bars.Count; i++)
        {
            min = Math.Min(min, _bars[i].LowTicks);
            max = Math.Max(max, _bars[i].HighTicks);
        }

        long pad = Math.Max(1, (max - min) / 20);
        var viewport = new ChartViewport(w, h, min - pad, max + pad, first, visible);

        _renderer.Render(canvas, viewport, _bars);

        if (_studies is { Enabled.Count: > 0 } studies)
        {
            _overlays.Render(canvas, viewport, _overlayData, studies.Enabled);
        }
    }
}
