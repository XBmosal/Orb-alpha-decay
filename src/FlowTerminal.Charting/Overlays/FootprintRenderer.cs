using FlowTerminal.Analytics.Footprints;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Renders footprint bars through the shared <see cref="ChartViewport"/> so they pan
/// and zoom exactly like the candle chart. Each visible bar draws a faint candle body,
/// per-price bid/ask bars (aggressive buys green on the right, sells light purple on
/// the left), the POC row, diagonal/stacked imbalance highlights, zero-print and
/// unfinished-auction marks, large-trade emphasis, per-cell numbers in the selected
/// mode, and a delta footer — all from the existing palette (no new colours). Detail
/// adapts to zoom (numbers → bars → silhouette). One canvas, no per-cell controls.
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

        long maxCellVol = 1;
        for (int i = 0; i < bars.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            foreach (var lvl in bars[i].Levels)
                maxCellVol = Math.Max(maxCellVol, Math.Max(lvl.BidVolume + lvl.AskVolume, lvl.TotalVolume));
        }

        using var pocFill = new SKPaint { Color = _palette.Warning.WithAlpha(46).ToSkColor() };
        using var bullBody = new SKPaint { Color = _palette.BullishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bearBody = new SKPaint { Color = _palette.BearishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bullWick = new SKPaint { Color = _palette.BullishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var bearWick = new SKPaint { Color = _palette.BearishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var askBar = new SKPaint { Color = _palette.BidLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // buys at ask → green
        using var bidBar = new SKPaint { Color = _palette.AskLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // sells at bid → purple
        using var buyOutline = new SKPaint { Color = _palette.BullishCandle.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
        using var sellOutline = new SKPaint { Color = _palette.BearishCandle.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
        using var zeroMark = new SKPaint { Color = _palette.MutedText.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        using var buyText = TextPaint(_palette.BullishCandle, 10);
        using var sellText = TextPaint(_palette.BearishCandle, 10);
        using var neutralText = TextPaint(_palette.Text, 10);

        bool legible = rowH >= 11f && slotW >= 58f;            // show per-cell numbers
        bool drawCells = rowH >= 1.0f && slotW >= 10f;         // show bid/ask bars (else silhouette)
        bool drawDetails = rowH >= 6f && slotW >= 24f;          // outlines/marks (need row height)
        float fontSize = Math.Clamp(rowH * 0.62f, 8f, 12f);
        buyText.TextSize = sellText.TextSize = neutralText.TextSize = fontSize;

        for (int c = 0; c < bars.Count; c++)
        {
            if (!vp.IsBarVisible(c)) continue;
            var bar = bars[c];
            float cx = vp.BarCenterX(c);
            float half = slotW * 0.46f;
            bool bull = bar.IsBullish;

            // Candle body + wick behind the cells.
            canvas.DrawLine(cx, vp.PriceToY(bar.HighTicks), cx, vp.PriceToY(bar.LowTicks), bull ? bullWick : bearWick);
            float yOpen = vp.PriceToY(bar.OpenTicks), yClose = vp.PriceToY(bar.CloseTicks);
            float top = Math.Min(yOpen, yClose), bot = Math.Max(yOpen, yClose);
            canvas.DrawRect(cx - half, top, half * 2f, Math.Max(1, bot - top), bull ? bullBody : bearBody);

            // Stacked-imbalance zones (faint background across the column's price span).
            foreach (var z in bar.StackedZones)
            {
                float zy1 = vp.PriceToY(z.HighTicks) - rowH / 2f;
                float zy2 = vp.PriceToY(z.LowTicks) + rowH / 2f;
                var col = (z.Side == ImbalanceSide.Buy ? _palette.BullishCandle : _palette.BearishCandle).WithAlpha(34);
                using var zfill = new SKPaint { Color = col.ToSkColor() };
                canvas.DrawRect(cx - half, zy1, half * 2f, Math.Max(1, zy2 - zy1), zfill);
            }

            // Sub-pixel rows: candle silhouette + POC only (no per-cell drawing) to stay fast.
            if (!drawCells)
            {
                if (s.ShowPoc && bar.PocTicks != long.MinValue)
                {
                    float y = vp.PriceToY(bar.PocTicks);
                    canvas.DrawRect(cx - half, y - rowH / 2f, half * 2f, Math.Max(1.5f, rowH), pocFill);
                }

                continue;
            }

            foreach (var lvl in bar.Levels)
            {
                if (lvl.PriceTicks < vp.MinPriceTicks || lvl.PriceTicks >= vp.MaxPriceTicks) continue;
                float y = vp.PriceToY(lvl.PriceTicks);
                float rh = Math.Max(1.5f, rowH * 0.86f);

                if (s.ShowPoc && lvl.IsPoc)
                    canvas.DrawRect(cx - half, y - rowH / 2f, half * 2f, rowH, pocFill);

                if (s.Mode == FootprintMode.VolumeProfile)
                {
                    float w = half * 1.8f * lvl.TotalVolume / maxCellVol;
                    var paint = lvl.Delta >= 0 ? askBar : bidBar;
                    if (w > 0.5f) canvas.DrawRect(cx - half, y - rh / 2f, w, rh, paint);
                }
                else
                {
                    float bidW = half * lvl.BidVolume / maxCellVol;
                    float askW = half * lvl.AskVolume / maxCellVol;
                    if (bidW > 0.5f) canvas.DrawRect(cx - bidW, y - rh / 2f, bidW, rh, bidBar);
                    if (askW > 0.5f) canvas.DrawRect(cx, y - rh / 2f, askW, rh, askBar);
                }

                // Imbalance outlines (dominant side), large-trade emphasis, zero-print mark.
                // Only when rows are tall enough that a thin outline reads as an outline.
                if (drawDetails)
                {
                    if (lvl.IsBuyImbalance)
                        canvas.DrawRect(cx, y - rh / 2f, half, rh, buyOutline);
                    if (lvl.IsSellImbalance)
                        canvas.DrawRect(cx - half, y - rh / 2f, half, rh, sellOutline);
                    if (s.ShowZeroPrints && lvl.IsZeroPrint)
                        canvas.DrawCircle(cx, y, Math.Min(rh, half) * 0.18f + 1.5f, zeroMark);
                    if (s.ShowLargeTrades && lvl.LargeTradeCount > 0)
                    {
                        using var lt = new SKPaint { Color = _palette.Text.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, IsAntialias = true };
                        canvas.DrawRect(cx - half, y - rh / 2f, half * 2f, rh, lt);
                    }
                }

                if (legible)
                {
                    var (text, paint) = CellText(lvl, s, buyText, sellText, neutralText);
                    if (text.Length > 0)
                    {
                        bool large = s.ShowLargeTrades && lvl.LargeTradeCount > 0;
                        paint.FakeBoldText = large;
                        canvas.DrawText(text, cx - half + 4, y + fontSize * 0.34f, paint);
                        paint.FakeBoldText = false;
                    }
                }
            }

            // Bar delta footer (green positive / light purple negative).
            if (legible)
            {
                using var footer = TextPaint(bar.Delta >= 0 ? _palette.BullishCandle : _palette.BearishCandle, Math.Clamp(fontSize, 8f, 11f));
                footer.TextAlign = SKTextAlign.Center;
                string d = (bar.Delta >= 0 ? "+" : "") + bar.Delta.ToString("N0");
                canvas.DrawText(d, cx, vp.PriceToY(bar.LowTicks) + footer.TextSize + 4, footer);
            }
        }

        if (!legible)
        {
            using var hint = TextPaint(_palette.MutedText, 11);
            canvas.DrawText("Footprint — zoom in (drag the time axis right) for cell numbers",
                vp.PlotLeft + 8, vp.PlotBottom - 8, hint);
        }
    }

    private static (string, SKPaint) CellText(in FootprintPriceLevel lvl, FootprintSettings s,
        SKPaint buyText, SKPaint sellText, SKPaint neutralText) => s.Mode switch
    {
        FootprintMode.BidAsk => ($"{lvl.BidVolume}×{lvl.AskVolume}", lvl.AskVolume >= lvl.BidVolume ? buyText : sellText),
        FootprintMode.Delta => ((lvl.Delta >= 0 ? "+" : "") + lvl.Delta, lvl.Delta >= 0 ? buyText : sellText),
        FootprintMode.TotalVolume => (lvl.TotalVolume.ToString("N0"), neutralText),
        FootprintMode.BidOnly => (lvl.BidVolume.ToString("N0"), sellText),
        FootprintMode.AskOnly => (lvl.AskVolume.ToString("N0"), buyText),
        FootprintMode.DeltaPercent => ($"{lvl.DeltaPercent:0}%", lvl.Delta >= 0 ? buyText : sellText),
        FootprintMode.TradeCount => (lvl.TradeCount.ToString("N0"), neutralText),
        FootprintMode.VolumeProfile => (string.Empty, neutralText),
        _ => ($"{lvl.BidVolume}×{lvl.AskVolume}", neutralText),
    };

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
}
