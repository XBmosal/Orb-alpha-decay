using FlowTerminal.Analytics.Footprints;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Renders footprint bars through the shared <see cref="ChartViewport"/> so they pan
/// and zoom exactly like the candle chart. The renderer is layered and composable: a
/// data mode (what each cell measures) is drawn through a visual layout (how the cell
/// is shown) with an optional background shading metric, then order-flow overlays (POC,
/// imbalances, stacked zones, zero prints, large trades) and a bar-delta footer. All
/// colours come from the existing palette — aggressive buys / positive delta render
/// green, sells / negative delta light purple, neutral volume grey; no red, thermal, or
/// rainbow. Detail adapts to zoom. One canvas, no per-cell controls.
/// </summary>
public sealed class FootprintRenderer
{
    private readonly ChartPalette _palette;

    public FootprintRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, ChartViewport vp, IReadOnlyList<FootprintBar> bars,
        FootprintSettings? settings = null, decimal tickSize = 0.25m)
    {
        var s = (settings ?? FootprintSettings.Default).Validate();
        if (bars.Count == 0)
        {
            DrawCenteredHint(canvas, vp, "Footprint — waiting for trades…");
            return;
        }

        float rowH = vp.PlotHeight / Math.Max(1, vp.MaxPriceTicks - vp.MinPriceTicks);
        float slotW = vp.BarSlotWidth;
        bool legible = rowH >= 11f && slotW >= 58f;
        bool drawCells = rowH >= 1.0f && slotW >= 10f;
        bool drawDetails = rowH >= 6f && slotW >= 24f;
        float fontSize = Math.Clamp(rowH * 0.62f, 8f, 12f);

        // Visible-range maxima (for VisibleRange normalisation, computed once).
        double rangeMaxMetric = 1, rangeMaxBg = 1;
        for (int i = 0; i < bars.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            foreach (var lvl in bars[i].Levels)
            {
                rangeMaxMetric = Math.Max(rangeMaxMetric, Metric(lvl, s));
                rangeMaxBg = Math.Max(rangeMaxBg, BgMagnitude(lvl, s.Background));
            }
        }

        using var p = new Paints(_palette, fontSize);

        for (int c = 0; c < bars.Count; c++)
        {
            if (!vp.IsBarVisible(c)) continue;
            var bar = bars[c];
            float cx = vp.BarCenterX(c);
            float half = slotW * 0.46f;

            DrawCandle(canvas, vp, bar, s, cx, half, p);
            DrawStackedZones(canvas, vp, bar, rowH, cx, half);

            if (!drawCells)
            {
                if (s.ShowPoc && bar.PocTicks != long.MinValue)
                    canvas.DrawRect(cx - half, vp.PriceToY(bar.PocTicks) - rowH / 2f, half * 2f, Math.Max(1.5f, rowH), p.PocFill);
                continue;
            }

            double barMaxMetric = 1, barMaxBg = 1;
            foreach (var lvl in bar.Levels)
            {
                barMaxMetric = Math.Max(barMaxMetric, Metric(lvl, s));
                barMaxBg = Math.Max(barMaxBg, BgMagnitude(lvl, s.Background));
            }

            double normMetric = s.Normalization == FootprintNormalization.VisibleRange ? rangeMaxMetric : barMaxMetric;
            double normBg = s.Normalization == FootprintNormalization.VisibleRange ? rangeMaxBg : barMaxBg;

            foreach (var lvl in bar.Levels)
            {
                if (lvl.PriceTicks < vp.MinPriceTicks || lvl.PriceTicks >= vp.MaxPriceTicks) continue;
                float y = vp.PriceToY(lvl.PriceTicks);
                float rh = Math.Max(1.5f, rowH * 0.86f);
                bool focus = lvl.IsPoc || lvl.IsBuyImbalance || lvl.IsSellImbalance || lvl.LargeTradeCount > 0;
                double dim = s.SubdueOrdinaryCells && !focus ? 0.25 : 1.0;

                DrawBackground(canvas, s, lvl, cx, half, y, rh, normBg, dim);
                if (s.ShowPoc && lvl.IsPoc)
                    canvas.DrawRect(cx - half, y - rowH / 2f, half * 2f, rowH, p.PocFill);

                DrawLayoutFill(canvas, s, lvl, cx, half, y, rh, normMetric, dim, p);

                if (drawDetails)
                    DrawCellOverlays(canvas, s, lvl, cx, half, y, rh, p);

                if (legible)
                    DrawCellText(canvas, s, lvl, cx, half, y, fontSize, dim, p);
            }

            if (legible && s.ShowDeltaFooter)
                DrawFooter(canvas, vp, bar, cx, fontSize);
        }

        if (!legible)
        {
            using var hint = TextPaint(_palette.MutedText, 11);
            canvas.DrawText("Footprint — zoom in (drag the time axis right) for cell numbers",
                vp.PlotLeft + 8, vp.PlotBottom - 8, hint);
        }
    }

    // ── Layers ──────────────────────────────────────────────────────────────

    private void DrawCandle(SKCanvas canvas, ChartViewport vp, FootprintBar bar, FootprintSettings s, float cx, float half, Paints p)
    {
        bool bull = s.CandleColor == CandleColorMode.DeltaDirection ? bar.Delta >= 0 : bar.IsBullish;
        if (s.ShowWick)
            canvas.DrawLine(cx, vp.PriceToY(bar.HighTicks), cx, vp.PriceToY(bar.LowTicks), bull ? p.BullWick : p.BearWick);
        if (s.ShowCandleBody)
        {
            float yOpen = vp.PriceToY(bar.OpenTicks), yClose = vp.PriceToY(bar.CloseTicks);
            float top = Math.Min(yOpen, yClose), bot = Math.Max(yOpen, yClose);
            canvas.DrawRect(cx - half, top, half * 2f, Math.Max(1, bot - top), bull ? p.BullBody : p.BearBody);
        }
    }

    private void DrawStackedZones(SKCanvas canvas, ChartViewport vp, FootprintBar bar, float rowH, float cx, float half)
    {
        foreach (var z in bar.StackedZones)
        {
            float zy1 = vp.PriceToY(z.HighTicks) - rowH / 2f;
            float zy2 = vp.PriceToY(z.LowTicks) + rowH / 2f;
            var col = (z.Side == ImbalanceSide.Buy ? _palette.BullishCandle : _palette.BearishCandle).WithAlpha(34);
            using var zfill = new SKPaint { Color = col.ToSkColor() };
            canvas.DrawRect(cx - half, zy1, half * 2f, Math.Max(1, zy2 - zy1), zfill);
        }
    }

    private void DrawBackground(SKCanvas canvas, FootprintSettings s, in FootprintPriceLevel lvl,
        float cx, float half, float y, float rh, double normBg, double dim)
    {
        if (s.Background == FootprintBackground.None && s.VisualLayout != FootprintVisualLayout.GradientCell)
            return;

        var src = s.Background == FootprintBackground.None ? FootprintBackground.TotalVolume : s.Background;
        double mag = BgMagnitude(lvl, src);
        if (mag <= 0) return;
        double intensity = Norm(mag, normBg, s.Normalization);
        var baseColor = BgColor(lvl, src);
        byte a = (byte)Math.Clamp(intensity * 150 * s.CellOpacity * dim, 0, 200);
        if (a < 6) return;
        using var bg = new SKPaint { Color = baseColor.WithAlpha(a).ToSkColor() };
        canvas.DrawRect(cx - half, y - rh / 2f, half * 2f, rh, bg);
    }

    private void DrawLayoutFill(SKCanvas canvas, FootprintSettings s, in FootprintPriceLevel lvl,
        float cx, float half, float y, float rh, double normMetric, double dim, Paints p)
    {
        switch (s.VisualLayout)
        {
            case FootprintVisualLayout.SplitText:
            case FootprintVisualLayout.SplitCell:
            case FootprintVisualLayout.Histogram:
            case FootprintVisualLayout.Ladder:
            {
                float bidW = (float)(half * Norm(lvl.BidVolume, normMetric, s.Normalization));
                float askW = (float)(half * Norm(lvl.AskVolume, normMetric, s.Normalization));
                Fill(canvas, cx - bidW, y - rh / 2f, bidW, rh, p.BidBar, dim);
                Fill(canvas, cx, y - rh / 2f, askW, rh, p.AskBar, dim);
                break;
            }

            case FootprintVisualLayout.MirroredHistogram:
            {
                double signed = SignedMetric(lvl, s.Mode);
                float w = (float)(half * Norm(Math.Abs(signed), normMetric, s.Normalization));
                if (signed >= 0) Fill(canvas, cx, y - rh / 2f, w, rh, p.AskBar, dim);        // buys right (green)
                else Fill(canvas, cx - w, y - rh / 2f, w, rh, p.BidBar, dim);                 // sells left (purple)
                break;
            }

            case FootprintVisualLayout.ProfileCandle:
            case FootprintVisualLayout.Hybrid:
            {
                double v = s.Mode == FootprintMode.VolumeProfile ? BgMagnitude(lvl, s.ProfileSource) : Metric(lvl, s);
                float w = (float)(half * 1.8 * Norm(v, normMetric, s.Normalization));
                Fill(canvas, cx - half, y - rh / 2f, w, rh, lvl.Delta >= 0 ? p.AskBar : p.BidBar, dim);
                break;
            }

            // GradientCell, SingleValue, TextOnly, OutlineCell, Marker draw no fill bar
            // (background + overlays + text carry them).
        }
    }

    private void DrawCellOverlays(SKCanvas canvas, FootprintSettings s, in FootprintPriceLevel lvl,
        float cx, float half, float y, float rh, Paints p)
    {
        if (lvl.IsBuyImbalance) canvas.DrawRect(cx, y - rh / 2f, half, rh, p.BuyOutline);
        if (lvl.IsSellImbalance) canvas.DrawRect(cx - half, y - rh / 2f, half, rh, p.SellOutline);
        if (s.ShowZeroPrints && lvl.IsZeroPrint) canvas.DrawCircle(cx, y, Math.Min(rh, half) * 0.18f + 1.5f, p.ZeroMark);
        if (s.ShowLargeTrades && lvl.LargeTradeCount > 0)
            canvas.DrawRect(cx - half, y - rh / 2f, half * 2f, rh, p.LargeOutline);
        if (s.VisualLayout == FootprintVisualLayout.OutlineCell && (lvl.IsBuyImbalance || lvl.IsSellImbalance))
        {
            var outline = lvl.IsBuyImbalance ? p.BuyOutline : p.SellOutline;
            canvas.DrawRect(cx - half, y - rh / 2f, half * 2f, rh, outline);
        }
        if (s.VisualLayout == FootprintVisualLayout.Marker && lvl.IsPoc)
            canvas.DrawCircle(cx, y, Math.Min(rh, half) * 0.3f, p.PocDot);
    }

    private void DrawCellText(SKCanvas canvas, FootprintSettings s, in FootprintPriceLevel lvl,
        float cx, float half, float y, float fontSize, double dim, Paints p)
    {
        // Visual-first layouts carry their data through fills/markers, not numbers.
        if (s.VisualLayout is FootprintVisualLayout.GradientCell or FootprintVisualLayout.Marker
            or FootprintVisualLayout.ProfileCandle)
        {
            return;
        }

        bool large = s.ShowLargeTrades && lvl.LargeTradeCount > 0;
        byte ta = (byte)Math.Clamp(255 * s.TextOpacity * dim, 30, 255);
        float yb = y + fontSize * 0.34f;

        if (s.VisualLayout == FootprintVisualLayout.Ladder)
        {
            // bid (left, purple) | ask (right, green), aligned columns.
            DrawText(canvas, lvl.BidVolume.ToString("N0"), cx - half + 3, yb, p.SellText, ta, large, SKTextAlign.Left);
            DrawText(canvas, lvl.AskVolume.ToString("N0"), cx + half - 3, yb, p.BuyText, ta, large, SKTextAlign.Right);
            return;
        }

        if (s.VisualLayout == FootprintVisualLayout.SplitText || s.VisualLayout == FootprintVisualLayout.SplitCell)
        {
            if (s.Mode == FootprintMode.BidAsk)
            {
                string sep = s.Separator switch { CellSeparator.Pipe => "|", CellSeparator.Slash => "/", _ => "×" };
                string text = s.HideZeros && lvl.TotalVolume == 0 ? "" : $"{lvl.BidVolume}{sep}{lvl.AskVolume}";
                var paint = lvl.AskVolume >= lvl.BidVolume ? p.BuyText : p.SellText;
                if (text.Length > 0) DrawText(canvas, text, cx - half + 4, yb, paint, ta, large, SKTextAlign.Left);
                return;
            }
        }

        // SingleValue / Hybrid / TextOnly / OutlineCell / SplitText-fallback: one centred value.
        var (txt, side) = CellText(lvl, s);
        if (txt.Length == 0) return;
        var col = side switch { 1 => p.BuyText, 2 => p.SellText, _ => p.NeutralText };
        var align = s.VisualLayout == FootprintVisualLayout.SingleValue || s.VisualLayout == FootprintVisualLayout.Hybrid
            ? SKTextAlign.Center : SKTextAlign.Left;
        float tx = align == SKTextAlign.Center ? cx : cx - half + 4;
        DrawText(canvas, txt, tx, yb, col, ta, large, align);
    }

    private void DrawFooter(SKCanvas canvas, ChartViewport vp, FootprintBar bar, float cx, float fontSize)
    {
        using var footer = TextPaint(bar.Delta >= 0 ? _palette.BullishCandle : _palette.BearishCandle, Math.Clamp(fontSize, 8f, 11f));
        footer.TextAlign = SKTextAlign.Center;
        string d = (bar.Delta >= 0 ? "+" : "") + bar.Delta.ToString("N0");
        canvas.DrawText(d, cx, vp.PriceToY(bar.LowTicks) + footer.TextSize + 4, footer);
    }

    // ── Metrics / text ──────────────────────────────────────────────────────

    private static double Metric(in FootprintPriceLevel l, FootprintSettings s) => s.Mode switch
    {
        FootprintMode.BidAsk or FootprintMode.TotalVolume or FootprintMode.VolumeProfile => l.TotalVolume,
        FootprintMode.Delta => Math.Abs(l.Delta),
        FootprintMode.BidOnly => l.BidVolume,
        FootprintMode.AskOnly => l.AskVolume,
        FootprintMode.DeltaPercent => Math.Abs(l.DeltaPercent),
        FootprintMode.TradeCount => l.TradeCount,
        _ => l.TotalVolume,
    };

    private static double SignedMetric(in FootprintPriceLevel l, FootprintMode mode) => mode switch
    {
        FootprintMode.DeltaPercent => l.DeltaPercent,
        _ => l.Delta,
    };

    private static double BgMagnitude(in FootprintPriceLevel l, FootprintBackground src) => src switch
    {
        FootprintBackground.BidVolume => l.BidVolume,
        FootprintBackground.AskVolume => l.AskVolume,
        FootprintBackground.TotalVolume => l.TotalVolume,
        FootprintBackground.Delta or FootprintBackground.AbsoluteDelta => Math.Abs(l.Delta),
        FootprintBackground.DeltaPercent => Math.Abs(l.DeltaPercent),
        FootprintBackground.TradeCount => l.TradeCount,
        _ => 0,
    };

    private RgbaColor BgColor(in FootprintPriceLevel l, FootprintBackground src) => src switch
    {
        FootprintBackground.BidVolume => _palette.AskLiquidity,     // bid → purple
        FootprintBackground.AskVolume => _palette.BidLiquidity,     // ask → green
        FootprintBackground.Delta or FootprintBackground.DeltaPercent => l.Delta >= 0 ? _palette.BullishCandle : _palette.BearishCandle,
        FootprintBackground.TotalVolume or FootprintBackground.AbsoluteDelta or FootprintBackground.TradeCount => _palette.NeutralVolume,
        _ => _palette.NeutralVolume,
    };

    /// <summary>Returns (text, side) where side 1=buy/green, 2=sell/purple, 0=neutral.</summary>
    private static (string, int) CellText(in FootprintPriceLevel l, FootprintSettings s) => s.Mode switch
    {
        FootprintMode.BidAsk => ($"{l.BidVolume}×{l.AskVolume}", l.AskVolume >= l.BidVolume ? 1 : 2),
        FootprintMode.Delta => ((l.Delta >= 0 ? "+" : "") + l.Delta, l.Delta >= 0 ? 1 : 2),
        FootprintMode.TotalVolume => (l.TotalVolume.ToString("N0"), 0),
        FootprintMode.BidOnly => (l.BidVolume.ToString("N0"), 2),
        FootprintMode.AskOnly => (l.AskVolume.ToString("N0"), 1),
        FootprintMode.DeltaPercent => ($"{l.DeltaPercent:0}%", l.Delta >= 0 ? 1 : 2),
        FootprintMode.TradeCount => (l.TradeCount.ToString("N0"), 0),
        FootprintMode.VolumeProfile => (string.Empty, 0),
        _ => ($"{l.BidVolume}×{l.AskVolume}", 0),
    };

    private static double Norm(double value, double max, FootprintNormalization norm)
    {
        if (max <= 0 || value <= 0) return 0;
        double r = norm switch
        {
            FootprintNormalization.Logarithmic => Math.Log(1 + value) / Math.Log(1 + max),
            FootprintNormalization.SquareRoot => Math.Sqrt(value) / Math.Sqrt(max),
            _ => value / max,
        };
        return Math.Clamp(r, 0, 1);
    }

    private static void Fill(SKCanvas canvas, float x, float y, float w, float h, SKPaint paint, double dim)
    {
        if (w <= 0.5f) return;
        if (dim >= 0.999)
        {
            canvas.DrawRect(x, y, w, h, paint);
            return;
        }

        var prev = paint.Color;
        paint.Color = prev.WithAlpha((byte)(prev.Alpha * dim));
        canvas.DrawRect(x, y, w, h, paint);
        paint.Color = prev;
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float yb, SKPaint paint, byte alpha, bool bold, SKTextAlign align)
    {
        paint.TextAlign = align;
        paint.FakeBoldText = bold;
        var prev = paint.Color;
        paint.Color = prev.WithAlpha(alpha);
        canvas.DrawText(text, x, yb, paint);
        paint.Color = prev;
        paint.FakeBoldText = false;
    }

    private void DrawCenteredHint(SKCanvas canvas, ChartViewport vp, string message)
    {
        using var hint = TextPaint(_palette.MutedText, 13);
        float w = hint.MeasureText(message);
        canvas.DrawText(message, vp.PlotLeft + (vp.PlotWidth - w) / 2f, vp.PlotTop + vp.PlotHeight / 2f, hint);
    }

    private static SKPaint TextPaint(RgbaColor color, float size) => new()
    {
        Color = color.ToSkColor(),
        IsAntialias = true,
        Typeface = SKTypeface.Default,
        TextSize = size,
    };

    /// <summary>Reusable paints for one render pass (no per-cell allocation in the hot path).</summary>
    private sealed class Paints : IDisposable
    {
        public readonly SKPaint PocFill, PocDot, BullBody, BearBody, BullWick, BearWick, AskBar, BidBar;
        public readonly SKPaint BuyOutline, SellOutline, ZeroMark, LargeOutline, BuyText, SellText, NeutralText;

        public Paints(ChartPalette pal, float fontSize)
        {
            PocFill = new SKPaint { Color = pal.Warning.WithAlpha(46).ToSkColor() };
            PocDot = new SKPaint { Color = pal.Warning.ToSkColor(), IsAntialias = true };
            BullBody = new SKPaint { Color = pal.BullishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
            BearBody = new SKPaint { Color = pal.BearishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
            BullWick = new SKPaint { Color = pal.BullishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
            BearWick = new SKPaint { Color = pal.BearishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
            AskBar = new SKPaint { Color = pal.BidLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // buys → green
            BidBar = new SKPaint { Color = pal.AskLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // sells → purple
            BuyOutline = new SKPaint { Color = pal.BullishCandle.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
            SellOutline = new SKPaint { Color = pal.BearishCandle.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
            ZeroMark = new SKPaint { Color = pal.MutedText.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
            LargeOutline = new SKPaint { Color = pal.Text.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true };
            BuyText = TextPaint(pal.BullishCandle, fontSize);
            SellText = TextPaint(pal.BearishCandle, fontSize);
            NeutralText = TextPaint(pal.Text, fontSize);
        }

        public void Dispose()
        {
            PocFill.Dispose(); PocDot.Dispose(); BullBody.Dispose(); BearBody.Dispose(); BullWick.Dispose();
            BearWick.Dispose(); AskBar.Dispose(); BidBar.Dispose(); BuyOutline.Dispose(); SellOutline.Dispose();
            ZeroMark.Dispose(); LargeOutline.Dispose(); BuyText.Dispose(); SellText.Dispose(); NeutralText.Dispose();
        }
    }
}
