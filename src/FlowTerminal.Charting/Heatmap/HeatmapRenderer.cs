using SkiaSharp;

namespace FlowTerminal.Charting.Heatmap;

/// <summary>
/// Renders the visible range of a <see cref="LiquidityHeatmap"/> onto an SKCanvas:
/// bid liquidity green, ask liquidity purple, brightness by normalized size. Only
/// the visible columns/prices are drawn; the heatmap model keeps history tiled so
/// the full session is never re-rasterized per update.
/// </summary>
public sealed class HeatmapRenderer
{
    private readonly ChartPalette _palette;

    public HeatmapRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(
        SKCanvas canvas, SKRect bounds, LiquidityHeatmap heatmap,
        long minPriceTicks, long maxPriceTicks, HeatmapScale scale = HeatmapScale.Percentile)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(heatmap);
        canvas.DrawRect(bounds, new SKPaint { Color = _palette.Background.ToSkColor() });

        var columns = heatmap.Columns;
        if (columns.Count == 0 || maxPriceTicks <= minPriceTicks) return;

        double scaleMax = heatmap.ComputeScaleMax(heatmap.BaseIndex, columns.Count, scale);
        float cw = bounds.Width / columns.Count;
        long priceSpan = maxPriceTicks - minPriceTicks;
        float ch = bounds.Height / priceSpan;

        var bid = _palette.BidLiquidity.ToSkColor();
        var ask = _palette.AskLiquidity.ToSkColor();
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        for (int c = 0; c < columns.Count; c++)
        {
            float x = bounds.Left + c * cw;
            var col = columns[c];
            DrawSide(canvas, col.Bid, bid, x, cw, ch, bounds, minPriceTicks, maxPriceTicks, scaleMax, scale, paint);
            DrawSide(canvas, col.Ask, ask, x, cw, ch, bounds, minPriceTicks, maxPriceTicks, scaleMax, scale, paint);
        }
    }

    private static void DrawSide(
        SKCanvas canvas, Dictionary<long, long> levels, SKColor baseColor,
        float x, float cw, float ch, SKRect bounds, long minP, long maxP,
        double scaleMax, HeatmapScale scale, SKPaint paint)
    {
        foreach (var (price, size) in levels)
        {
            if (price < minP || price >= maxP) continue;
            double intensity = LiquidityHeatmap.Intensity(size, scaleMax, scale);
            if (intensity <= 0.01) continue;

            float y = bounds.Bottom - (price - minP + 1) * ch;
            paint.Color = baseColor.WithAlpha((byte)(intensity * 230));
            canvas.DrawRect(x, y, cw + 0.6f, ch + 0.6f, paint);
        }
    }
}
