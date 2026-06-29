using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Renders a footprint / cluster chart through a shared <see cref="ChartViewport"/>,
/// so it pans and zooms exactly like the candle chart. Each visible column shows
/// per-price bid×ask execution volume as horizontal bars inside a candle outline:
/// aggressive buys (ask side) extend right in green, sells (bid side) extend left in
/// purple, and the column's point-of-control row is shaded amber. Per-cell numbers
/// overlay when there is room; otherwise the colored bars alone still read as a
/// cluster, so the chart is never empty. One canvas, no per-cell controls.
/// </summary>
public sealed class FootprintRenderer
{
    private readonly ChartPalette _palette;

    public FootprintRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, ChartViewport vp, IReadOnlyList<FootprintColumn> columns, decimal tickSize = 0.25m)
    {
        if (columns.Count == 0)
        {
            DrawCenteredHint(canvas, vp, "Footprint — waiting for trades…");
            return;
        }

        float rowH = vp.PlotHeight / Math.Max(1, vp.MaxPriceTicks - vp.MinPriceTicks);
        float slotW = vp.BarSlotWidth;

        long maxCellVol = 1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            foreach (var cell in columns[i].Cells)
            {
                maxCellVol = Math.Max(maxCellVol, cell.BidVolume + cell.AskVolume);
            }
        }

        using var pocFill = new SKPaint { Color = _palette.Warning.WithAlpha(46).ToSkColor() };
        using var bullBody = new SKPaint { Color = _palette.BullishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bearBody = new SKPaint { Color = _palette.BearishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bullWick = new SKPaint { Color = _palette.BullishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var bearWick = new SKPaint { Color = _palette.BearishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var askBar = new SKPaint { Color = _palette.BidLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // buys at ask → green, right
        using var bidBar = new SKPaint { Color = _palette.AskLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // sells at bid → purple, left
        using var buyText = TextPaint(_palette.BullishCandle, 10);
        using var sellText = TextPaint(_palette.BearishCandle, 10);

        bool legible = rowH >= 11f && slotW >= 58f;
        float fontSize = Math.Clamp(rowH * 0.62f, 8f, 12f);
        buyText.TextSize = sellText.TextSize = fontSize;

        for (int c = 0; c < columns.Count; c++)
        {
            if (!vp.IsBarVisible(c)) continue;
            var col = columns[c];
            float cx = vp.BarCenterX(c);
            float half = slotW * 0.46f;
            bool bull = col.CloseTicks >= col.OpenTicks;

            canvas.DrawLine(cx, vp.PriceToY(col.HighTicks), cx, vp.PriceToY(col.LowTicks), bull ? bullWick : bearWick);
            float yOpen = vp.PriceToY(col.OpenTicks), yClose = vp.PriceToY(col.CloseTicks);
            float top = Math.Min(yOpen, yClose), bot = Math.Max(yOpen, yClose);
            canvas.DrawRect(cx - half, top, half * 2f, Math.Max(1, bot - top), bull ? bullBody : bearBody);

            long poc = -1, pocVol = -1;
            foreach (var cell in col.Cells)
            {
                long tot = cell.BidVolume + cell.AskVolume;
                if (tot > pocVol) { pocVol = tot; poc = cell.PriceTicks; }
            }

            foreach (var cell in col.Cells)
            {
                if (cell.PriceTicks < vp.MinPriceTicks || cell.PriceTicks >= vp.MaxPriceTicks) continue;
                float y = vp.PriceToY(cell.PriceTicks);
                float rh = Math.Max(1.5f, rowH * 0.86f);

                if (cell.PriceTicks == poc)
                {
                    canvas.DrawRect(cx - half, y - rowH / 2f, half * 2f, rowH, pocFill);
                }

                float bidW = half * cell.BidVolume / maxCellVol;
                float askW = half * cell.AskVolume / maxCellVol;
                if (bidW > 0.5f) canvas.DrawRect(cx - bidW, y - rh / 2f, bidW, rh, bidBar);
                if (askW > 0.5f) canvas.DrawRect(cx, y - rh / 2f, askW, rh, askBar);

                if (legible)
                {
                    var paint = cell.AskVolume >= cell.BidVolume ? buyText : sellText;
                    canvas.DrawText($"{cell.BidVolume}×{cell.AskVolume}", cx - half + 4, y + fontSize * 0.34f, paint);
                }
            }
        }

        if (!legible)
        {
            using var hint = TextPaint(_palette.MutedText, 11);
            canvas.DrawText("Footprint — zoom in (drag the time axis right) for cell numbers",
                vp.PlotLeft + 8, vp.PlotBottom - 8, hint);
        }
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
}
