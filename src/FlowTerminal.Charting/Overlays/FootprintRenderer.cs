using System.Globalization;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Renders a footprint / cluster chart: a small number of wide columns, each showing
/// per-price bid×ask execution volume as horizontal bars inside a candle outline.
/// Aggressive buys (ask side) extend right in green; aggressive sells (bid side)
/// extend left in purple; each column's point-of-control row is shaded amber. The
/// per-price cell numbers are overlaid when there is room for them; otherwise the
/// colored bars alone still read as a cluster, so the chart is never empty. One
/// canvas, no per-cell controls.
/// </summary>
public sealed class FootprintRenderer
{
    private const float RightGutter = 64f; // price axis on the right

    private readonly ChartPalette _palette;

    public FootprintRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, SKRect bounds, IReadOnlyList<FootprintColumn> columns, decimal tickSize = 0.25m)
    {
        canvas.Clear(_palette.Background.ToSkColor());
        if (columns.Count == 0)
        {
            DrawCenteredHint(canvas, bounds, "Footprint — waiting for trades…");
            return;
        }

        long min = long.MaxValue, max = long.MinValue;
        long maxCellVol = 1;
        foreach (var col in columns)
        {
            min = Math.Min(min, col.LowTicks);
            max = Math.Max(max, col.HighTicks);
            foreach (var cell in col.Cells)
            {
                maxCellVol = Math.Max(maxCellVol, cell.BidVolume + cell.AskVolume);
            }
        }

        if (max <= min)
        {
            DrawCenteredHint(canvas, bounds, "Footprint — waiting for trades…");
            return;
        }

        long pad = Math.Max(1, (max - min) / 20);
        min -= pad;
        max += pad;

        float plotRight = bounds.Right - RightGutter;
        float plotW = plotRight - bounds.Left;
        float slotW = plotW / columns.Count;
        float rowH = bounds.Height / (max - min);

        float Ticks2Y(long p) => bounds.Bottom - (p - min) * rowH;

        using var pocFill = new SKPaint { Color = _palette.Warning.WithAlpha(46).ToSkColor() };
        using var bullBody = new SKPaint { Color = _palette.BullishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bearBody = new SKPaint { Color = _palette.BearishCandle.WithAlpha(26).ToSkColor(), IsAntialias = true };
        using var bullWick = new SKPaint { Color = _palette.BullishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var bearWick = new SKPaint { Color = _palette.BearishCandle.WithAlpha(150).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var askBar = new SKPaint { Color = _palette.BidLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // buys at ask → green, right
        using var bidBar = new SKPaint { Color = _palette.AskLiquidity.WithAlpha(210).ToSkColor(), IsAntialias = true };  // sells at bid → purple, left
        using var gridPaint = new SKPaint { Color = _palette.GridLine.WithAlpha(120).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var axisText = TextPaint(_palette.MutedText, 10);
        using var buyText = TextPaint(_palette.BullishCandle, 10);
        using var sellText = TextPaint(_palette.BearishCandle, 10);

        // Numbers only when each row is tall enough and columns wide enough.
        bool legible = rowH >= 11f && slotW >= 58f;
        float fontSize = Math.Clamp(rowH * 0.62f, 8f, 12f);
        buyText.TextSize = sellText.TextSize = fontSize;

        DrawPriceAxis(canvas, bounds, plotRight, min, max, rowH, tickSize, gridPaint, axisText);

        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            float x = bounds.Left + c * slotW;
            float cx = x + slotW / 2f;
            float half = slotW * 0.46f;
            bool bull = col.CloseTicks >= col.OpenTicks;

            // Candle wick (high→low) and a faint body so the column reads as a candle.
            canvas.DrawLine(cx, Ticks2Y(col.HighTicks), cx, Ticks2Y(col.LowTicks), bull ? bullWick : bearWick);
            float yOpen = Ticks2Y(col.OpenTicks), yClose = Ticks2Y(col.CloseTicks);
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
                if (cell.PriceTicks < min || cell.PriceTicks >= max) continue;
                float y = Ticks2Y(cell.PriceTicks);
                float rh = Math.Max(1.5f, rowH * 0.86f);

                if (cell.PriceTicks == poc)
                {
                    canvas.DrawRect(x + 1, y - rowH / 2f, slotW - 2, rowH, pocFill);
                }

                // Bid (sells) extend left, ask (buys) extend right, sized vs the max cell.
                float bidW = half * cell.BidVolume / maxCellVol;
                float askW = half * cell.AskVolume / maxCellVol;
                if (bidW > 0.5f) canvas.DrawRect(cx - bidW, y - rh / 2f, bidW, rh, bidBar);
                if (askW > 0.5f) canvas.DrawRect(cx, y - rh / 2f, askW, rh, askBar);

                if (legible)
                {
                    var paint = cell.AskVolume >= cell.BidVolume ? buyText : sellText;
                    canvas.DrawText($"{cell.BidVolume}×{cell.AskVolume}", x + 5, y + fontSize * 0.34f, paint);
                }
            }
        }

        if (!legible)
        {
            using var hint = TextPaint(_palette.MutedText, 11);
            canvas.DrawText("Footprint — widen the panel or use fewer bars to see cell numbers",
                bounds.Left + 8, bounds.Bottom - 8, hint);
        }
    }

    private void DrawPriceAxis(
        SKCanvas canvas, SKRect bounds, float plotRight, long min, long max, float rowH,
        decimal tickSize, SKPaint grid, SKPaint text)
    {
        // ~6 evenly spaced price labels down the right gutter.
        const int lines = 6;
        long span = max - min;
        for (int i = 0; i <= lines; i++)
        {
            long priceTicks = min + span * i / lines;
            float y = bounds.Bottom - (priceTicks - min) * rowH;
            canvas.DrawLine(bounds.Left, y, plotRight, y, grid);
            decimal price = priceTicks * tickSize;
            canvas.DrawText(price.ToString("N2", CultureInfo.InvariantCulture), plotRight + 6, y + 3.5f, text);
        }
    }

    private void DrawCenteredHint(SKCanvas canvas, SKRect bounds, string message)
    {
        using var hint = TextPaint(_palette.MutedText, 13);
        float w = hint.MeasureText(message);
        canvas.DrawText(message, bounds.MidX - w / 2f, bounds.MidY, hint);
    }

    private static SKPaint TextPaint(RgbaColor color, float size) => new()
    {
        Color = color.ToSkColor(),
        IsAntialias = true,
        Typeface = SKTypeface.Default,
        TextSize = size,
    };
}
