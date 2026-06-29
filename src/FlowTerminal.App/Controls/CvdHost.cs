using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Renders the cumulative-volume-delta history as a sparkline or candlesticks on a
/// single Skia canvas. Observational only; rising CVD is green, falling light purple.
/// </summary>
public sealed class CvdHost : SKElement
{
    public enum Mode { Line, Candles }

    private readonly CvdRenderer _renderer = new();
    private readonly ChartPalette _palette = ChartPalette.Default;
    private IReadOnlyList<CvdBar> _bars = Array.Empty<CvdBar>();

    public Mode DisplayMode { get; set; } = Mode.Line;

    public void Update(IReadOnlyList<CvdBar> bars)
    {
        _bars = bars;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(_palette.PanelBackground.ToSkColor());
        float w = e.Info.Width, h = e.Info.Height;
        if (w <= 0 || h <= 0 || _bars.Count == 0) return;

        var rect = new SKRect(8, 6, w - 8, h - 6);
        bool ok = RenderSafety.Guard(canvas, new SKRect(0, 0, w, h), () =>
        {
            if (DisplayMode == Mode.Candles) _renderer.RenderCandles(canvas, rect, _bars);
            else _renderer.RenderLine(canvas, rect, _bars);
        }, _palette, "CVD view paused");
        if (!ok) RenderGuard.LogThrottled("cvd");
    }
}
