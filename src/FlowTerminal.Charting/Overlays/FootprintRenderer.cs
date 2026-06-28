using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Renders a footprint / cluster chart: a small number of wide columns, each showing
/// per-price bid×ask execution volume inside a candle outline. Buy-dominant cells
/// are green, sell-dominant purple, and each column's point-of-control row is
/// shaded amber. One canvas, no per-cell controls. Drawn only when there is room
/// for the numbers (otherwise the host falls back to normal candles).
/// </summary>
public sealed class FootprintRenderer
{
    private readonly ChartPalette _palette;

    public FootprintRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, SKRect bounds, IReadOnlyList<FootprintColumn> columns)
    {
        canvas.Clear(_palette.Background.ToSkColor());
        if (columns.Count == 0) return;

        long min = long.MaxValue, max = long.MinValue;
        foreach (var col in columns)
        {
            min = Math.Min(min, col.LowTicks);
            max = Math.Max(max, col.HighTicks);
        }

        if (max <= min) return;
        long pad = Math.Max(1, (max - min) / 20);
        min -= pad; max += pad;

        float plotW = bounds.Width - 56; // leave a right price gutter
        float slotW = plotW / columns.Count;
        float rowH = bounds.Height / (max - min);

        float ticks2y(long p) => bounds.Bottom - (p - min) * rowH;

        using var amber = new SKPaint { Color = _palette.Warning.WithAlpha(30).ToSkColor() };
        using var outline = new SKPaint { Color = _palette.GridLine.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        using var buyText = TextPaint(_palette.BullishCandle);
        using var sellText = TextPaint(_palette.BearishCandle);
        float fontSize = Math.Clamp(rowH * 0.72f, 7f, 12f);
        buyText.TextSize = sellText.TextSize = fontSize;

        bool legible = rowH >= 9f && slotW >= 46f;

        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            float x = bounds.Left + c * slotW;
            float cx = x + slotW / 2f;

            // Candle outline (body).
            float yOpen = ticks2y(col.OpenTicks), yClose = ticks2y(col.CloseTicks);
            float top = Math.Min(yOpen, yClose), bot = Math.Max(yOpen, yClose);
            canvas.DrawRect(cx - slotW * 0.42f, top, slotW * 0.84f, Math.Max(1, bot - top), outline);

            long poc = -1, pocVol = -1;
            foreach (var cell in col.Cells)
            {
                long tot = cell.BidVolume + cell.AskVolume;
                if (tot > pocVol) { pocVol = tot; poc = cell.PriceTicks; }
            }

            foreach (var cell in col.Cells)
            {
                if (cell.PriceTicks < min || cell.PriceTicks >= max) continue;
                float y = ticks2y(cell.PriceTicks);
                if (cell.PriceTicks == poc)
                {
                    canvas.DrawRect(x + 1, y - rowH / 2f, slotW - 2, rowH, amber);
                }

                if (!legible) continue;
                var paint = cell.AskVolume >= cell.BidVolume ? buyText : sellText;
                string text = $"{cell.BidVolume}×{cell.AskVolume}";
                canvas.DrawText(text, x + 4, y + fontSize * 0.35f, paint);
            }
        }

        if (!legible)
        {
            using var hint = TextPaint(_palette.MutedText);
            hint.TextSize = 12;
            canvas.DrawText("Footprint — widen the chart / fewer bars for cell numbers", bounds.Left + 8, bounds.Top + 18, hint);
        }
    }

    private static SKPaint TextPaint(RgbaColor color) => new()
    {
        Color = color.ToSkColor(),
        IsAntialias = true,
        Typeface = SKTypeface.Default,
        TextSize = 11,
    };
}
