using System.Globalization;
using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Charting.BigTrades;
using FlowTerminal.Domain.Events;
using SkiaSharp;

namespace FlowTerminal.Charting.Heatmap;

/// <summary>
/// One executed trade plotted as a bubble on the heatmap. <see cref="Side"/> carries the
/// aggressor classification: an unknown-side trade is drawn neutral, never forced to sell.
/// </summary>
public readonly record struct TradeDot(DateTime Time, long PriceTicks, long Quantity, AggressorSide Side)
{
    public bool IsBuy => Side == AggressorSide.Buy;
}

/// <summary>
/// Mutable interaction state for the heatmap: price/time zoom and pan (pan is in
/// fractions so the host needs no knowledge of the rendered scale) plus the crosshair
/// cursor in pixels. Defaults mean "auto-follow the live market at 1× zoom".
/// </summary>
public sealed class BookmapView
{
    public double PriceZoom { get; set; } = 1.0;     // &gt;1 = zoom in (narrower price span)
    public double PricePanFrac { get; set; }          // shift center, in fractions of the half-span
    public double TimeZoom { get; set; } = 1.0;       // &gt;1 = fewer columns (zoom in)
    public double TimePanFrac { get; set; }           // scroll back, in fractions of the visible window
    public SKPoint? Cursor { get; set; }              // crosshair position in pixels (null = hidden)

    public void Reset()
    {
        PriceZoom = 1.0;
        PricePanFrac = 0;
        TimeZoom = 1.0;
        TimePanFrac = 0;
    }
}

/// <summary>
/// Renders a full Bookmap-style liquidity view on one Skia canvas: a time×price
/// heatmap of resting order-book size (bid green, ask light purple, brightening to a
/// hot near-white core for the heaviest levels), bold lines for large resting
/// orders, the traced last-price line, executed-trade bubbles drawn as shaded
/// spheres (green buys / purple sells, area ∝ aggressive volume), the best-bid/ask
/// boundary, a last-trade marker, a per-time volume histogram, price/time axes, and
/// an intensity legend. Observational only.
/// </summary>
public sealed class BookmapRenderer
{
    private const float RightAxis = 64f;
    private const float BottomAxis = 20f;
    private const float VolStrip = 48f;

    private readonly ChartPalette _palette;
    private readonly Dictionary<long, long> _merge = new(); // reused per column (no per-frame alloc)
    private readonly BigTradeRenderer _bigTradeRenderer;

    public BookmapRenderer(ChartPalette? palette = null)
    {
        _palette = palette ?? ChartPalette.Default;
        _bigTradeRenderer = new BigTradeRenderer(_palette);
    }

    /// <param name="minSize">Hide resting levels smaller than this many contracts (contrast filter).</param>
    public void Render(
        SKCanvas canvas, SKRect bounds, LiquidityHeatmap heatmap, IReadOnlyList<TradeDot> trades,
        long bestBidTicks, long bestAskTicks, long lastPriceTicks,
        long autoMinTicks, long autoMaxTicks, decimal tickSize,
        long minSize = 0, BookmapView? view = null, long bestBidSize = 0, long bestAskSize = 0,
        HeatmapScale scale = HeatmapScale.Percentile,
        IReadOnlyList<BigTradeGroup>? bigTrades = null)
    {
        canvas.Clear(_palette.Background.ToSkColor());

        float plotRight = bounds.Right - RightAxis;
        float timeAxisTop = bounds.Bottom - BottomAxis;
        float volTop = timeAxisTop - VolStrip;
        var plot = new SKRect(bounds.Left, bounds.Top, plotRight, volTop);

        var columns = heatmap.Columns;
        if (columns.Count == 0 || autoMaxTicks <= autoMinTicks)
        {
            DrawCenteredHint(canvas, plot, "Waiting for depth data…");
            return;
        }

        var v = view ?? new BookmapView();

        // Apply price zoom/pan to the auto-framed window.
        double halfSpan = Math.Max(1.0, (autoMaxTicks - autoMinTicks) / 2.0) / Math.Clamp(v.PriceZoom, 0.05, 50);
        double center = (autoMinTicks + autoMaxTicks) / 2.0 + v.PricePanFrac * halfSpan;
        long minPriceTicks = (long)Math.Floor(center - halfSpan);
        long maxPriceTicks = (long)Math.Ceiling(center + halfSpan);
        if (maxPriceTicks <= minPriceTicks) maxPriceTicks = minPriceTicks + 1;

        long span = maxPriceTicks - minPriceTicks;
        float ch = plot.Height / span;
        float Y(long price) => plot.Bottom - (price - minPriceTicks) * ch;

        // Time zoom/pan: choose a visible column window and scroll it back from the edge.
        int baseCols = Math.Max(60, (int)(plot.Width / 3f));
        int minWin = Math.Min(20, columns.Count);
        int visN = Math.Clamp((int)(baseCols / Math.Clamp(v.TimeZoom, 0.05, 50)), minWin, columns.Count);
        int panCols = (int)(v.TimePanFrac * visN);
        int from = Math.Clamp(columns.Count - visN - panCols, 0, Math.Max(0, columns.Count - visN));
        float cw = plot.Width / visN;
        DateTime t0 = columns[from].TimestampUtc, t1 = columns[from + visN - 1].TimestampUtc;

        // ── Heatmap cells ────────────────────────────────────────────────
        // Color is by position relative to that column's mid — everything below the
        // price is green, everything above is purple — regardless of which book side
        // the (possibly carried-forward) size came from. Per-level size = bid+ask.
        double scaleMax = heatmap.ComputeScaleMax(from, visN, scale);
        var bidBase = _palette.BidLiquidity.ToSkColor();
        var askBase = _palette.AskLiquidity.ToSkColor();
        using var cell = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
        for (int c = 0; c < visN; c++)
        {
            float x = plot.Left + c * cw;
            var col = columns[from + c];
            long mid = ColumnMid(col, minPriceTicks, maxPriceTicks);

            _merge.Clear();
            Accumulate(col.Bid, minPriceTicks, maxPriceTicks);
            Accumulate(col.Ask, minPriceTicks, maxPriceTicks);
            foreach (var (price, size) in _merge)
            {
                if (size < minSize) continue; // contrast filter
                double intensity = LiquidityHeatmap.Intensity(size, scaleMax, scale);
                if (intensity <= 0.012) continue;
                cell.Color = Heat(price < mid ? bidBase : askBase, intensity);
                canvas.DrawRect(x, Y(price) - ch, cw + 0.6f, ch + 0.6f, cell);
            }
        }

        DrawRestingWalls(canvas, plot, columns[^1], minPriceTicks, maxPriceTicks, minSize, ch, Y);
        DrawPriceTrail(canvas, plot, t0, t1, trades, minPriceTicks, maxPriceTicks, Y);
        DrawTradeBubblesAndVolume(canvas, plot, volTop, timeAxisTop, t0, t1, trades, minPriceTicks, maxPriceTicks, Y);

        // Big Trades: emphasise qualifying groups over the faint all-trade context, using the
        // heatmap's own time/price mapping so they align exactly. Reconciles with the shared
        // detection engine (no separate heatmap big-trade logic).
        if (bigTrades is { Count: > 0 })
        {
            _bigTradeRenderer.Render(canvas, plot, bigTrades, t0, t1, minPriceTicks, maxPriceTicks, tickSize);
        }

        DrawLevelLine(canvas, plot, Y, bestBidTicks, minPriceTicks, maxPriceTicks, _palette.BullishCandle);
        DrawLevelLine(canvas, plot, Y, bestAskTicks, minPriceTicks, maxPriceTicks, _palette.BearishCandle);

        DrawPriceAxis(canvas, plot, plotRight, bounds.Right, minPriceTicks, maxPriceTicks, ch, tickSize,
            lastPriceTicks, lastPriceTicks >= (bestBidTicks + bestAskTicks) / 2);
        DrawTimeAxis(canvas, plot, timeAxisTop, bounds.Bottom, t0, t1);
        DrawLegend(canvas, plot);
        DrawSizeBox(canvas, plot, plotRight, bounds.Right, Y, bestBidTicks, bestAskTicks, bestBidSize, bestAskSize, minPriceTicks, maxPriceTicks);
        DrawCrosshair(canvas, plot, v.Cursor, columns, from, visN, cw, ch, minPriceTicks, maxPriceTicks, tickSize, Y);
    }

    private void DrawSizeBox(
        SKCanvas canvas, SKRect plot, float axisX, float right, Func<long, float> y,
        long bid, long ask, long bidSize, long askSize, long minP, long maxP)
    {
        void Box(long price, long size, RgbaColor color, bool up)
        {
            if (price < minP || price >= maxP || size <= 0) return;
            float cy = y(price);
            string s = size.ToString("N0", CultureInfo.InvariantCulture);
            using var fill = new SKPaint { Color = color.WithAlpha(235).ToSkColor(), IsAntialias = true };
            using var label = Text(new RgbaColor(0x0A, 0x0C, 0x11, 0xFF), 10f);
            label.Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            float boxY = up ? cy - 21 : cy + 3;
            canvas.DrawRoundRect(new SKRect(axisX + 2, boxY, right - 2, boxY + 16), 3, 3, fill);
            canvas.DrawText(s, axisX + 8, boxY + 12, label);
        }

        Box(ask, askSize, _palette.BearishCandle, up: true);
        Box(bid, bidSize, _palette.BullishCandle, up: false);
    }

    private void DrawCrosshair(
        SKCanvas canvas, SKRect plot, SKPoint? cursor, IReadOnlyList<HeatmapColumn> columns,
        int from, int visN, float cw, float ch, long minP, long maxP, decimal tickSize, Func<long, float> y)
    {
        if (cursor is not { } cur || !plot.Contains(cur)) return;

        using var line = new SKPaint { Color = _palette.Text.WithAlpha(90).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        canvas.DrawLine(cur.X, plot.Top, cur.X, plot.Bottom, line);
        canvas.DrawLine(plot.Left, cur.Y, plot.Right, cur.Y, line);

        // Resolve the cell under the cursor and read its resting size.
        long price = minP + (long)Math.Round((plot.Bottom - cur.Y) / ch);
        int ci = Math.Clamp(from + (int)((cur.X - plot.Left) / cw), 0, columns.Count - 1);
        long size = columns[ci].BidAt(price) + columns[ci].AskAt(price);

        string text = $"{(price * tickSize).ToString("N2", CultureInfo.InvariantCulture)}   ·   {size:N0}";
        using var bg = new SKPaint { Color = _palette.PanelBackground.WithAlpha(240).ToSkColor(), IsAntialias = true };
        using var border = new SKPaint { Color = _palette.GridLine.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        using var label = Text(_palette.Text, 11f);
        float w = label.MeasureText(text) + 16;
        float bx = Math.Min(cur.X + 10, plot.Right - w);
        float by = Math.Max(plot.Top, cur.Y - 26);
        var box = new SKRect(bx, by, bx + w, by + 19);
        canvas.DrawRoundRect(box, 4, 4, bg);
        canvas.DrawRoundRect(box, 4, 4, border);
        canvas.DrawText(text, bx + 8, by + 13.5f, label);
    }

    private void Accumulate(Dictionary<long, long> levels, long minP, long maxP)
    {
        foreach (var (price, size) in levels)
        {
            if (price < minP || price >= maxP || size <= 0) continue;
            _merge[price] = _merge.GetValueOrDefault(price) + size;
        }
    }

    /// <summary>The price boundary for a column: midpoint of its best bid and best ask.</summary>
    private static long ColumnMid(HeatmapColumn col, long minP, long maxP)
    {
        long bestBid = long.MinValue, bestAsk = long.MaxValue;
        foreach (var (p, s) in col.Bid) if (s > 0 && p > bestBid) bestBid = p;
        foreach (var (p, s) in col.Ask) if (s > 0 && p < bestAsk) bestAsk = p;
        if (bestBid == long.MinValue && bestAsk == long.MaxValue) return (minP + maxP) / 2;
        if (bestBid == long.MinValue) return bestAsk;
        if (bestAsk == long.MaxValue) return bestBid;
        return (bestBid + bestAsk) / 2;
    }

    /// <summary>Draws the largest current resting levels as bold horizontal "wall" lines.</summary>
    private void DrawRestingWalls(
        SKCanvas canvas, SKRect plot, HeatmapColumn latest, long minP, long maxP, long minSize, float ch, Func<long, float> y)
    {
        long max = 1;
        foreach (var s in latest.Bid.Values) max = Math.Max(max, s);
        foreach (var s in latest.Ask.Values) max = Math.Max(max, s);
        long threshold = Math.Max(minSize, (long)(max * 0.55));
        long mid = ColumnMid(latest, minP, maxP);
        float h = Math.Max(2f, ch);

        void Walls(Dictionary<long, long> levels)
        {
            using var p = new SKPaint { IsAntialias = false };
            foreach (var (price, size) in levels)
            {
                if (price < minP || price >= maxP || size < threshold) continue;
                double t = Math.Min(1.0, size / (double)max);
                // Below the current price → green, above → purple (by position, not book side).
                var color = price < mid ? _palette.BullishCandle : _palette.BearishCandle;
                p.Color = color.WithAlpha((byte)(150 + 90 * t)).ToSkColor();
                canvas.DrawRect(plot.Left, y(price) - h / 2f, plot.Width, h, p);
            }
        }

        Walls(latest.Bid);
        Walls(latest.Ask);
    }

    /// <summary>Traces the executed price over time as a thin light line (the bubbles ride it).</summary>
    private void DrawPriceTrail(
        SKCanvas canvas, SKRect plot, DateTime t0, DateTime t1, IReadOnlyList<TradeDot> trades,
        long minP, long maxP, Func<long, float> y)
    {
        if (trades.Count < 2) return;
        double totalSec = Math.Max(0.001, (t1 - t0).TotalSeconds);
        using var path = new SKPath();
        bool started = false;
        foreach (var tr in trades)
        {
            if (tr.Time < t0) continue;
            float x = plot.Left + (float)((tr.Time - t0).TotalSeconds / totalSec) * plot.Width;
            float yy = y(Math.Clamp(tr.PriceTicks, minP, maxP - 1));
            if (!started) { path.MoveTo(x, yy); started = true; } else path.LineTo(x, yy);
        }

        if (!started) return;
        using var line = new SKPaint { Color = _palette.Text.WithAlpha(110).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawPath(path, line);
    }

    private void DrawTradeBubblesAndVolume(
        SKCanvas canvas, SKRect plot, float volTop, float volBottom, DateTime t0, DateTime t1,
        IReadOnlyList<TradeDot> trades, long minP, long maxP, Func<long, float> y)
    {
        if (trades.Count == 0) return;
        double totalSec = Math.Max(0.001, (t1 - t0).TotalSeconds);
        int buckets = Math.Max(1, (int)(plot.Width / 6f));
        int denom = Math.Max(1, buckets - 1);

        var agg = new Dictionary<(int Bucket, long Price), (long Buy, long Sell, long Unknown)>();
        var vol = new (long Buy, long Sell, long Unknown)[buckets];
        foreach (var tr in trades)
        {
            if (tr.Time < t0) continue;
            int b = (int)Math.Clamp((tr.Time - t0).TotalSeconds / totalSec * denom, 0, denom);
            Add(ref vol[b], tr.Side, tr.Quantity);
            if (tr.PriceTicks < minP || tr.PriceTicks >= maxP) continue;
            var key = (b, tr.PriceTicks);
            var cur = agg.GetValueOrDefault(key);
            Add(ref cur, tr.Side, tr.Quantity);
            agg[key] = cur;
        }

        // Volume histogram along the bottom (buy green over sell purple over unknown gray, stacked).
        long maxVolBar = 1;
        for (int i = 0; i < buckets; i++) maxVolBar = Math.Max(maxVolBar, vol[i].Buy + vol[i].Sell + vol[i].Unknown);
        float barW = Math.Max(1f, plot.Width / buckets);
        float stripH = volBottom - volTop;
        using var vBuy = new SKPaint { Color = _palette.BullishCandle.WithAlpha(200).ToSkColor() };
        using var vSell = new SKPaint { Color = _palette.BearishCandle.WithAlpha(200).ToSkColor() };
        using var vUnk = new SKPaint { Color = _palette.NeutralVolume.WithAlpha(190).ToSkColor() };
        for (int i = 0; i < buckets; i++)
        {
            long total = vol[i].Buy + vol[i].Sell + vol[i].Unknown;
            if (total <= 0) continue;
            float x = plot.Left + (float)i / denom * plot.Width - barW / 2f;
            float hTot = stripH * (total / (float)maxVolBar);
            float hBuy = hTot * (vol[i].Buy / (float)total);
            float hSell = hTot * (vol[i].Sell / (float)total);
            canvas.DrawRect(x, volBottom - hBuy, barW, hBuy, vBuy);
            canvas.DrawRect(x, volBottom - hBuy - hSell, barW, hSell, vSell);
            canvas.DrawRect(x, volBottom - hTot, barW, hTot - hBuy - hSell, vUnk);
        }

        // Aggregated trade bubbles as shaded spheres (dominant side decides colour; unknown neutral).
        long maxBubble = 1;
        foreach (var v in agg.Values) maxBubble = Math.Max(maxBubble, v.Buy + v.Sell + v.Unknown);
        foreach (var (key, v) in agg)
        {
            long total = v.Buy + v.Sell + v.Unknown;
            if (total <= 0) continue;
            float r = (float)Math.Clamp(2.4 + Math.Sqrt(total / (double)maxBubble) * 16.0, 2.4, 18.0);
            float x = plot.Left + (float)key.Bucket / denom * plot.Width;
            DrawSphere(canvas, x, y(key.Price), r, DominantColor(v.Buy, v.Sell, v.Unknown));
        }
    }

    private static void Add(ref (long Buy, long Sell, long Unknown) acc, AggressorSide side, long qty)
    {
        switch (side)
        {
            case AggressorSide.Buy: acc.Buy += qty; break;
            case AggressorSide.Sell: acc.Sell += qty; break;
            default: acc.Unknown += qty; break;
        }
    }

    private RgbaColor DominantColor(long buy, long sell, long unknown)
    {
        if (unknown > buy && unknown > sell) return _palette.NeutralVolume;
        return buy >= sell ? _palette.BullishCandle : _palette.BearishCandle;
    }

    private void DrawSphere(SKCanvas canvas, float cx, float cy, float r, RgbaColor baseColor)
    {
        var bc = baseColor.ToSkColor();
        var light = Lerp(bc, new SKColor(255, 255, 255), 0.55);
        var dark = Lerp(bc, new SKColor(0, 0, 0), 0.45);
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - r * 0.32f, cy - r * 0.32f), r * 1.25f,
            new[] { light, bc, dark }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, paint);
        using var rim = new SKPaint { Color = dark.WithAlpha(160), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, rim);
    }

    private static SKColor Lerp(SKColor a, SKColor b, double f) => new(
        (byte)(a.Red + (b.Red - a.Red) * f),
        (byte)(a.Green + (b.Green - a.Green) * f),
        (byte)(a.Blue + (b.Blue - a.Blue) * f));

    private static SKColor Heat(SKColor c, double intensity)
    {
        // Strong contrast: small orders stay faint, large orders are opaque and a touch
        // deeper/richer so they read as the dominant resting liquidity.
        double i = Math.Clamp(intensity, 0.0, 1.0);
        double a = Math.Pow(i, 1.6);                       // small → very faint, large → solid
        double dark = 1.0 - 0.18 * Math.Max(0.0, (i - 0.7) / 0.3); // biggest get slightly darker
        byte Sc(byte ch) => (byte)(ch * dark);
        return new SKColor(Sc(c.Red), Sc(c.Green), Sc(c.Blue), (byte)(a * 255));
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

    private void DrawTimeAxis(SKCanvas canvas, SKRect plot, float axisTop, float bottom, DateTime t0, DateTime t1)
    {
        using var grid = new SKPaint { Color = _palette.GridLine.WithAlpha(90).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = Text(_palette.MutedText, 10);
        canvas.DrawLine(plot.Left, axisTop, plot.Right, axisTop, grid);
        const int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            float x = plot.Left + plot.Width * i / ticks;
            var t = t0.AddSeconds((t1 - t0).TotalSeconds * i / ticks).ToLocalTime();
            string s = t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            float w = text.MeasureText(s);
            canvas.DrawText(s, Math.Clamp(x - w / 2f, plot.Left, plot.Right - w), bottom - 6, text);
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
