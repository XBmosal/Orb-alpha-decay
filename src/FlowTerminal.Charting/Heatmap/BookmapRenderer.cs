using System.Globalization;
using SkiaSharp;

namespace FlowTerminal.Charting.Heatmap;

/// <summary>One executed trade plotted as a bubble on the heatmap.</summary>
public readonly record struct TradeDot(DateTime Time, long PriceTicks, long Quantity, bool IsBuy);

/// <summary>
/// Renders a full Bookmap-style liquidity view on one Skia canvas: a time×price
/// heatmap of resting order-book size (bid green, ask light purple, brightening to a
/// hot near-white core for the heaviest levels), executed-trade bubbles (green buys /
/// purple sells, area ∝ size), the live best-bid/ask boundary, a last-trade marker,
/// price and time axes, and an intensity legend. Observational only.
/// </summary>
public sealed class BookmapRenderer
{
    private const float RightAxis = 64f;
    private const float BottomAxis = 20f;

    private readonly ChartPalette _palette;

    public BookmapRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(
        SKCanvas canvas, SKRect bounds, LiquidityHeatmap heatmap, IReadOnlyList<TradeDot> trades,
        long bestBidTicks, long bestAskTicks, long lastPriceTicks,
        long minPriceTicks, long maxPriceTicks, decimal tickSize,
        HeatmapScale scale = HeatmapScale.Percentile)
    {
        canvas.Clear(_palette.Background.ToSkColor());

        float plotRight = bounds.Right - RightAxis;
        float plotBottom = bounds.Bottom - BottomAxis;
        var plot = new SKRect(bounds.Left, bounds.Top, plotRight, plotBottom);

        var columns = heatmap.Columns;
        if (columns.Count == 0 || maxPriceTicks <= minPriceTicks)
        {
            DrawCenteredHint(canvas, plot, "Waiting for depth data…");
            return;
        }

        long span = maxPriceTicks - minPriceTicks;
        float ch = plot.Height / span;
        float cw = plot.Width / columns.Count;
        float Y(long price) => plot.Bottom - (price - minPriceTicks) * ch;

        // ── Heatmap cells ────────────────────────────────────────────────
        double scaleMax = heatmap.ComputeScaleMax(heatmap.BaseIndex, columns.Count, scale);
        var bidBase = _palette.BidLiquidity.ToSkColor();
        var askBase = _palette.AskLiquidity.ToSkColor();
        using var cell = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        for (int c = 0; c < columns.Count; c++)
        {
            float x = plot.Left + c * cw;
            var col = columns[c];
            DrawSide(canvas, col.Bid, bidBase, x, cw, ch, minPriceTicks, maxPriceTicks, scaleMax, scale, Y, cell);
            DrawSide(canvas, col.Ask, askBase, x, cw, ch, minPriceTicks, maxPriceTicks, scaleMax, scale, Y, cell);
        }

        // ── Trade bubbles ────────────────────────────────────────────────
        if (trades.Count > 0)
        {
            DateTime t0 = columns[0].TimestampUtc;
            DateTime t1 = columns[^1].TimestampUtc;
            double totalSec = Math.Max(0.001, (t1 - t0).TotalSeconds);
            long maxQty = 1;
            foreach (var tr in trades) maxQty = Math.Max(maxQty, tr.Quantity);

            using var buy = new SKPaint { Color = _palette.BullishCandle.WithAlpha(180).ToSkColor(), IsAntialias = true };
            using var sell = new SKPaint { Color = _palette.BearishCandle.WithAlpha(180).ToSkColor(), IsAntialias = true };
            foreach (var tr in trades)
            {
                if (tr.PriceTicks < minPriceTicks || tr.PriceTicks >= maxPriceTicks) continue;
                if (tr.Time < t0) continue;
                float x = plot.Left + (float)((tr.Time - t0).TotalSeconds / totalSec) * plot.Width;
                float r = (float)Math.Clamp(2.0 + Math.Sqrt(tr.Quantity / (double)maxQty) * 11.0, 2.0, 13.0);
                canvas.DrawCircle(x, Y(tr.PriceTicks), r, tr.IsBuy ? buy : sell);
            }
        }

        // ── Best bid / ask boundary + last trade ─────────────────────────
        DrawLevelLine(canvas, plot, Y, bestBidTicks, minPriceTicks, maxPriceTicks, _palette.BullishCandle);
        DrawLevelLine(canvas, plot, Y, bestAskTicks, minPriceTicks, maxPriceTicks, _palette.BearishCandle);

        // ── Axes ─────────────────────────────────────────────────────────
        DrawPriceAxis(canvas, plot, plotRight, bounds.Right, minPriceTicks, maxPriceTicks, ch, tickSize,
            lastPriceTicks, lastPriceTicks >= (bestBidTicks + bestAskTicks) / 2);
        DrawTimeAxis(canvas, plot, columns[0].TimestampUtc, columns[^1].TimestampUtc);
        DrawLegend(canvas, plot);
    }

    private void DrawSide(
        SKCanvas canvas, Dictionary<long, long> levels, SKColor baseColor,
        float x, float cw, float ch, long minP, long maxP, double scaleMax,
        HeatmapScale scale, Func<long, float> y, SKPaint paint)
    {
        foreach (var (price, size) in levels)
        {
            if (price < minP || price >= maxP) continue;
            double intensity = LiquidityHeatmap.Intensity(size, scaleMax, scale);
            if (intensity <= 0.015) continue;
            paint.Color = Heat(baseColor, intensity);
            canvas.DrawRect(x, y(price) - ch, cw + 0.6f, ch + 0.6f, paint);
        }
    }

    /// <summary>Maps intensity to a heat color: dim at low size, brightening to a near-white core.</summary>
    private static SKColor Heat(SKColor c, double intensity)
    {
        double a = Math.Min(1.0, intensity * 1.15);
        double w = Math.Max(0.0, (intensity - 0.68) / 0.32) * 0.85; // white-hot blend above ~0.68
        byte Mix(byte ch) => (byte)(ch + (255 - ch) * w);
        return new SKColor(Mix(c.Red), Mix(c.Green), Mix(c.Blue), (byte)(a * 235));
    }

    private static void DrawLevelLine(SKCanvas canvas, SKRect plot, Func<long, float> y, long price, long minP, long maxP, RgbaColor color)
    {
        if (price == long.MinValue || price < minP || price >= maxP) return;
        using var p = new SKPaint { Color = color.WithAlpha(120).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(plot.Left, y(price), plot.Right, y(price), p);
    }

    private void DrawPriceAxis(
        SKCanvas canvas, SKRect plot, float axisX, float right, long minP, long maxP, float ch,
        decimal tickSize, long lastPrice, bool lastUp)
    {
        using var grid = new SKPaint { Color = _palette.GridLine.WithAlpha(90).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = Text(_palette.MutedText, 10);
        long span = maxP - minP;
        const int lines = 8;
        for (int i = 0; i <= lines; i++)
        {
            long pt = minP + span * i / lines;
            float y = plot.Bottom - (pt - minP) * ch;
            canvas.DrawLine(plot.Left, y, axisX, y, grid);
            canvas.DrawText((pt * tickSize).ToString("N2", CultureInfo.InvariantCulture), axisX + 6, y + 3.5f, text);
        }

        if (lastPrice >= minP && lastPrice < maxP)
        {
            float y = plot.Bottom - (lastPrice - minP) * ch;
            var color = (lastUp ? _palette.BullishCandle : _palette.BearishCandle).ToSkColor();
            using var pill = new SKPaint { Color = color, IsAntialias = true };
            using var label = Text(new RgbaColor(0x0A, 0x0C, 0x11, 0xFF), 10.5f);
            label.Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            string s = (lastPrice * tickSize).ToString("N2", CultureInfo.InvariantCulture);
            canvas.DrawRoundRect(new SKRect(axisX + 2, y - 8, right - 2, y + 9), 3, 3, pill);
            canvas.DrawText(s, axisX + 8, y + 4, label);
        }
    }

    private void DrawTimeAxis(SKCanvas canvas, SKRect plot, DateTime t0, DateTime t1)
    {
        using var grid = new SKPaint { Color = _palette.GridLine.WithAlpha(90).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = Text(_palette.MutedText, 10);
        canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom, grid);
        const int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            float x = plot.Left + plot.Width * i / ticks;
            var t = t0.AddSeconds((t1 - t0).TotalSeconds * i / ticks).ToLocalTime();
            string s = t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            float w = text.MeasureText(s);
            canvas.DrawText(s, Math.Clamp(x - w / 2f, plot.Left, plot.Right - w), plot.Bottom + 14, text);
        }
    }

    private void DrawLegend(SKCanvas canvas, SKRect plot)
    {
        float x = plot.Left + 10, y = plot.Top + 12, w = 90, h = 8;
        using var text = Text(_palette.MutedText, 9.5f);
        canvas.DrawText("LIQUIDITY", x, y - 3, text);
        for (int i = 0; i < (int)w; i++)
        {
            double t = i / w;
            using var p = new SKPaint { Color = Heat(_palette.BidLiquidity.ToSkColor(), t) };
            canvas.DrawRect(x + i, y, 1.2f, h, p);
        }

        canvas.DrawText("low", x, y + h + 10, text);
        float hw = text.MeasureText("high");
        canvas.DrawText("high", x + w - hw, y + h + 10, text);
    }

    private void DrawCenteredHint(SKCanvas canvas, SKRect plot, string message)
    {
        using var hint = Text(_palette.MutedText, 13);
        float w = hint.MeasureText(message);
        canvas.DrawText(message, plot.MidX - w / 2f, plot.MidY, hint);
    }

    private static SKPaint Text(RgbaColor color, float size) => new()
    {
        Color = color.ToSkColor(),
        IsAntialias = true,
        TextSize = size,
        Typeface = SKTypeface.Default,
    };
}
