using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Single-canvas liquidity heatmap control. It delegates painting to the live feed,
/// which renders the tiled history under its own lock — there are no per-cell WPF
/// controls and the full session is never re-rasterized per frame.
/// </summary>
public sealed class HeatmapHost : SKElement
{
    private LiveFeedService? _feed;

    public void Attach(LiveFeedService feed)
    {
        _feed = feed;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        if (_feed is null)
        {
            canvas.Clear();
            return;
        }

        _feed.RenderHeatmap(canvas, new SkiaSharp.SKRect(0, 0, e.Info.Width, e.Info.Height));
    }
}
