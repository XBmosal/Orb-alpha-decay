using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// A single SkiaSharp-backed candlestick chart control. The entire chart is drawn
/// on one canvas — there is NO WPF control per candle. The latest bar snapshot is
/// supplied from the (off-thread) feed and the control repaints on a coalesced
/// timer, so market processing is never tied to the visual frame rate.
/// </summary>
public sealed class SkiaChartHost : SKElement
{
    private readonly CandlestickRenderer _renderer = new();
    private IReadOnlyList<Bar> _bars = Array.Empty<Bar>();
    private int _maxVisibleBars = 120;

    public void UpdateBars(IReadOnlyList<Bar> bars)
    {
        _bars = bars;
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
    }
}
