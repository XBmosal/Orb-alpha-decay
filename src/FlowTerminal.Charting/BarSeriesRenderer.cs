using FlowTerminal.Analytics.Bars;
using SkiaSharp;

namespace FlowTerminal.Charting;

/// <summary>The price-series presentation styles the chart can render.</summary>
public enum ChartType
{
    Candles,
    Bars,
    Line,
}

/// <summary>
/// Renders the non-candle price series styles (OHLC bars and a close line) onto an
/// <see cref="SKCanvas"/> using the same viewport and palette as the candlestick
/// renderer. Bullish elements are green (#22C55E), bearish light purple (#C4A7FF);
/// red is never used. Only the visible bar range is drawn; no per-bar allocations.
/// </summary>
public sealed class BarSeriesRenderer : IDisposable
{
    private readonly ChartPalette _palette;
    private readonly SKPaint _bull;
    private readonly SKPaint _bear;
    private readonly SKPaint _line;
    private readonly SKPaint _lineFill;
    private readonly SKPaint _background;

    public BarSeriesRenderer(ChartPalette? palette = null)
    {
        _palette = palette ?? ChartPalette.Default;
        _bull = Stroke(_palette.BullishCandle, 1.5f);
        _bear = Stroke(_palette.BearishCandle, 1.5f);
        _line = Stroke(_palette.SelectedObject, 1.8f); // cyan close line
        _lineFill = new SKPaint { Color = _palette.SelectedObject.WithAlpha(28).ToSkColor(), Style = SKPaintStyle.Fill, IsAntialias = true };
        _background = new SKPaint { Color = _palette.Background.ToSkColor(), Style = SKPaintStyle.Fill };
    }

    public ChartPalette Palette => _palette;

    /// <summary>OHLC bars: a high–low stick with a left open tick and right close tick.</summary>
    public void RenderBars(SKCanvas canvas, ChartViewport vp, IReadOnlyList<Bar> bars)
    {
        ClearPlot(canvas, vp);
        float tick = Math.Max(1.5f, vp.BarSlotWidth * 0.32f);

        for (int i = 0; i < bars.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            var bar = bars[i];
            var paint = bar.CloseTicks >= bar.OpenTicks ? _bull : _bear;
            float cx = vp.BarCenterX(i);
            canvas.DrawLine(cx, vp.PriceToY(bar.HighTicks), cx, vp.PriceToY(bar.LowTicks), paint);
            float yOpen = vp.PriceToY(bar.OpenTicks);
            float yClose = vp.PriceToY(bar.CloseTicks);
            canvas.DrawLine(cx - tick, yOpen, cx, yOpen, paint);
            canvas.DrawLine(cx, yClose, cx + tick, yClose, paint);
        }
    }

    /// <summary>A single close-price line with a soft fill down to the plot floor.</summary>
    public void RenderLine(SKCanvas canvas, ChartViewport vp, IReadOnlyList<Bar> bars)
    {
        ClearPlot(canvas, vp);

        using var path = new SKPath();
        using var fill = new SKPath();
        bool started = false;
        float firstX = 0, lastX = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            float x = vp.BarCenterX(i);
            float y = vp.PriceToY(bars[i].CloseTicks);
            if (!started)
            {
                path.MoveTo(x, y);
                fill.MoveTo(x, vp.PlotBottom);
                fill.LineTo(x, y);
                firstX = x;
                started = true;
            }
            else
            {
                path.LineTo(x, y);
                fill.LineTo(x, y);
            }

            lastX = x;
        }

        if (!started) return;
        fill.LineTo(lastX, vp.PlotBottom);
        fill.LineTo(firstX, vp.PlotBottom);
        fill.Close();
        canvas.DrawPath(fill, _lineFill);
        canvas.DrawPath(path, _line);
    }

    private void ClearPlot(SKCanvas canvas, ChartViewport vp)
    {
        canvas.Clear(_palette.Background.ToSkColor());
        canvas.DrawRect(vp.PlotLeft, vp.PlotTop, vp.PlotWidth, vp.PlotHeight, _background);
    }

    private static SKPaint Stroke(RgbaColor c, float w) => new()
    {
        Color = c.ToSkColor(),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = w,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
    };

    public void Dispose()
    {
        _bull.Dispose();
        _bear.Dispose();
        _line.Dispose();
        _lineFill.Dispose();
        _background.Dispose();
    }
}
