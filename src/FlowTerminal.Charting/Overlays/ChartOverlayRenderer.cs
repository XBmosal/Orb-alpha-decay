using FlowTerminal.Analytics.PriceAction;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Draws study overlays onto the price chart on top of the candles: a volume
/// profile (right-side histogram, green buy / purple sell, amber POC), the VWAP
/// line, fair-value-gap boxes, and opening-range high/low lines. Which overlays
/// draw is controlled by the set of enabled study short-codes.
/// </summary>
public sealed class ChartOverlayRenderer
{
    private readonly ChartPalette _palette;

    public ChartOverlayRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, ChartViewport vp, ChartOverlays overlays, IReadOnlySet<string> enabled)
    {
        if (enabled.Contains("VBP") || enabled.Contains("BAC") || enabled.Contains("DP"))
        {
            DrawProfile(canvas, vp, overlays);
        }

        if (enabled.Contains("ORB"))
        {
            DrawOrb(canvas, vp, overlays);
        }

        if (enabled.Contains("FVG"))
        {
            DrawFvgs(canvas, vp, overlays);
        }

        if (enabled.Contains("VWAP"))
        {
            DrawVwap(canvas, vp, overlays);
        }
    }

    private void DrawProfile(SKCanvas canvas, ChartViewport vp, ChartOverlays o)
    {
        if (o.Profile.Count == 0) return;

        long max = 1;
        foreach (var lvl in o.Profile) max = Math.Max(max, lvl.TotalVolume);

        float maxWidth = vp.PlotWidth * 0.28f;
        float rowH = Math.Max(1f, vp.PlotHeight / Math.Max(1, vp.MaxPriceTicks - vp.MinPriceTicks));

        using var buy = new SKPaint { Color = _palette.BullishCandle.WithAlpha(110).ToSkColor(), IsAntialias = false };
        using var sell = new SKPaint { Color = _palette.BearishCandle.WithAlpha(110).ToSkColor(), IsAntialias = false };

        foreach (var lvl in o.Profile)
        {
            if (lvl.PriceTicks < vp.MinPriceTicks || lvl.PriceTicks >= vp.MaxPriceTicks) continue;
            float y = vp.PriceToY(lvl.PriceTicks) - rowH / 2f;
            float w = maxWidth * (lvl.TotalVolume / (float)max);
            float bw = lvl.TotalVolume > 0 ? w * (lvl.BuyVolume / (float)lvl.TotalVolume) : 0;
            float x = vp.PlotRight - w;
            canvas.DrawRect(x, y, bw, rowH, buy);
            canvas.DrawRect(x + bw, y, w - bw, rowH, sell);
        }

        DrawHLine(canvas, vp, o.PocTicks, _palette.Warning, 1.6f);
        DrawHLine(canvas, vp, o.VahTicks, _palette.MutedText, 1f);
        DrawHLine(canvas, vp, o.ValTicks, _palette.MutedText, 1f);
    }

    private void DrawVwap(SKCanvas canvas, ChartViewport vp, ChartOverlays o)
    {
        if (o.VwapByBar.Count < 2) return;
        using var paint = new SKPaint
        {
            Color = _palette.SelectedObject.ToSkColor(), // cyan
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true,
        };
        using var path = new SKPath();
        bool started = false;
        for (int i = 0; i < o.VwapByBar.Count; i++)
        {
            if (!vp.IsBarVisible(i) || double.IsNaN(o.VwapByBar[i])) continue;
            float x = vp.BarCenterX(i);
            float y = vp.PriceToY((long)Math.Round(o.VwapByBar[i]));
            if (!started) { path.MoveTo(x, y); started = true; }
            else path.LineTo(x, y);
        }

        canvas.DrawPath(path, paint);
    }

    private void DrawFvgs(SKCanvas canvas, ChartViewport vp, ChartOverlays o)
    {
        foreach (var fvg in o.Fvgs)
        {
            var color = fvg.Direction == GapDirection.Bullish ? _palette.BullishCandle : _palette.BearishCandle;
            using var fill = new SKPaint { Color = color.WithAlpha(40).ToSkColor(), IsAntialias = false };
            float left = vp.IsBarVisible(fvg.BarIndex) ? vp.BarCenterX(fvg.BarIndex) : vp.PlotLeft;
            float top = vp.PriceToY(fvg.TopTicks);
            float bottom = vp.PriceToY(fvg.BottomTicks);
            canvas.DrawRect(left, top, vp.PlotRight - left, bottom - top, fill);
        }
    }

    private void DrawOrb(SKCanvas canvas, ChartViewport vp, ChartOverlays o)
    {
        if (o.OrbHighTicks is { } hi) DrawHLine(canvas, vp, hi, _palette.AskLiquidity, 1.4f);
        if (o.OrbLowTicks is { } lo) DrawHLine(canvas, vp, lo, _palette.BidLiquidity, 1.4f);
    }

    private static void DrawHLine(SKCanvas canvas, ChartViewport vp, long priceTicks, RgbaColor color, float width)
    {
        if (priceTicks == long.MinValue || priceTicks < vp.MinPriceTicks || priceTicks >= vp.MaxPriceTicks) return;
        float y = vp.PriceToY(priceTicks);
        using var paint = new SKPaint { Color = color.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
        canvas.DrawLine(vp.PlotLeft, y, vp.PlotRight, y, paint);
    }
}
