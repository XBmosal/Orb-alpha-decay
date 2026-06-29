using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>One bar of cumulative-volume-delta: the running CVD's open/high/low/close over a bar.</summary>
public readonly record struct CvdBar(long Open, long High, long Low, long Close);

/// <summary>
/// Renders the cumulative-volume-delta history as either a sparkline (area line with
/// a zero baseline) or candlesticks. Rising CVD is green (#22C55E); falling is light
/// purple (#C4A7FF) — never red. A zero reference line anchors the scale.
/// </summary>
public sealed class CvdRenderer
{
    private readonly ChartPalette _palette;

    public CvdRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void RenderLine(SKCanvas canvas, SKRect b, IReadOnlyList<CvdBar> bars)
    {
        if (!Scale(bars, b, useWick: false, out var map, out float zeroY)) return;

        using var path = new SKPath();
        for (int i = 0; i < bars.Count; i++)
        {
            float x = X(b, bars.Count, i);
            float y = map(bars[i].Close);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }

        // Two-tone fill: green above the zero line, purple below.
        using var fill = new SKPath(path);
        fill.LineTo(X(b, bars.Count, bars.Count - 1), zeroY);
        fill.LineTo(X(b, bars.Count, 0), zeroY);
        fill.Close();

        using var greenFill = new SKPaint { Color = _palette.BullishCandle.WithAlpha(40).ToSkColor(), IsAntialias = true };
        using var purpleFill = new SKPaint { Color = _palette.BearishCandle.WithAlpha(40).ToSkColor(), IsAntialias = true };
        canvas.Save(); canvas.ClipRect(new SKRect(b.Left, b.Top, b.Right, zeroY)); canvas.DrawPath(fill, greenFill); canvas.Restore();
        canvas.Save(); canvas.ClipRect(new SKRect(b.Left, zeroY, b.Right, b.Bottom)); canvas.DrawPath(fill, purpleFill); canvas.Restore();

        DrawZero(canvas, b, zeroY);

        var lastUp = bars[^1].Close >= 0;
        using var line = new SKPaint
        {
            Color = (lastUp ? _palette.BullishCandle : _palette.BearishCandle).ToSkColor(),
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true,
        };
        canvas.DrawPath(path, line);
    }

    public void RenderCandles(SKCanvas canvas, SKRect b, IReadOnlyList<CvdBar> bars)
    {
        if (!Scale(bars, b, useWick: true, out var map, out float zeroY)) return;
        DrawZero(canvas, b, zeroY);

        float slot = (b.Width - 8) / Math.Max(1, bars.Count);
        float bodyW = Math.Max(1f, slot * 0.6f);
        using var bull = new SKPaint { Color = _palette.BullishCandle.ToSkColor(), IsAntialias = true };
        using var bear = new SKPaint { Color = _palette.BearishCandle.ToSkColor(), IsAntialias = true };
        using var bullWick = new SKPaint { Color = _palette.BullishCandle.WithAlpha(170).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        using var bearWick = new SKPaint { Color = _palette.BearishCandle.WithAlpha(170).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            bool up = bar.Close >= bar.Open;
            float x = X(b, bars.Count, i);
            canvas.DrawLine(x, map(bar.High), x, map(bar.Low), up ? bullWick : bearWick);
            float yO = map(bar.Open), yC = map(bar.Close);
            float top = Math.Min(yO, yC), h = Math.Max(1f, Math.Abs(yC - yO));
            canvas.DrawRect(x - bodyW / 2f, top, bodyW, h, up ? bull : bear);
        }
    }

    private static float X(SKRect b, int count, int i) =>
        b.Left + 4 + (count <= 1 ? b.Width / 2f : (b.Width - 8) * i / (count - 1));

    private void DrawZero(SKCanvas canvas, SKRect b, float zeroY)
    {
        using var z = new SKPaint { Color = _palette.GridLine.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawLine(b.Left, zeroY, b.Right, zeroY, z);
    }

    private static bool Scale(IReadOnlyList<CvdBar> bars, SKRect b, bool useWick, out Func<long, float> map, out float zeroY)
    {
        map = _ => b.MidY;
        zeroY = b.MidY;
        if (bars.Count == 0 || b.Height <= 0) return false;

        long min = 0, max = 0; // always include zero so the baseline is in view
        foreach (var bar in bars)
        {
            long hi = useWick ? bar.High : Math.Max(bar.Open, bar.Close);
            long lo = useWick ? bar.Low : Math.Min(bar.Open, bar.Close);
            min = Math.Min(min, lo);
            max = Math.Max(max, hi);
        }

        if (max == min) max = min + 1;
        float top = b.Top + 6, bottom = b.Bottom - 6;
        long range = max - min;
        map = v => bottom - (float)(v - min) / range * (bottom - top);
        zeroY = map(0);
        return true;
    }
}
