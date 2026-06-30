using FlowTerminal.Domain.Instruments;
using SkiaSharp;

namespace FlowTerminal.Charting.Dom;

/// <summary>
/// Renders the read-only DOM ladder on one Skia canvas (no per-cell WPF controls).
/// A resolved column list (from a <see cref="DomPreset"/>) drives the layout: the price
/// column is the visual centre, bid columns sit left, ask columns right. Resting size
/// shows a subtle depth bar (bid green, ask light purple); the inside market is
/// emphasised (best bid green, best ask light purple); executed volume, cumulative
/// depth, pull/stack and refills are text columns; walls get a restrained outline. All
/// colours come from the existing palette — strictly observational, labelled READ ONLY.
/// </summary>
public sealed class DomLadderRenderer
{
    private readonly ChartPalette _palette;

    public DomLadderRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, SKRect bounds, IReadOnlyList<DomRow> rows,
        IReadOnlyList<DomColumnType> columns, decimal tickSize, InstrumentSpec? spec = null)
    {
        canvas.Clear(_palette.PanelBackground.ToSkColor());
        if (rows.Count == 0 || columns.Count == 0)
        {
            DrawCentered(canvas, bounds, "Waiting for market depth…", _palette.MutedText, 13f);
            return;
        }

        const float headerH = 20f;
        const float pad = 8f;
        float left = bounds.Left + pad, right = bounds.Right - pad;
        float totalW = right - left;

        // Column widths: scale the descriptors' default widths to fit the panel.
        var descs = new DomColumnDescriptor[columns.Count];
        float wantW = 0;
        for (int i = 0; i < columns.Count; i++) { descs[i] = DomColumnRegistry.For(columns[i]); wantW += (float)descs[i].DefaultWidth; }
        float scale = wantW > 0 ? totalW / wantW : 1f;

        var x = new float[columns.Count + 1];
        x[0] = left;
        for (int i = 0; i < columns.Count; i++) x[i + 1] = x[i] + (float)descs[i].DefaultWidth * scale;

        float rowH = Math.Max(12f, (bounds.Height - headerH - pad) / rows.Count);
        long maxSize = 1;
        foreach (var r in rows) maxSize = Math.Max(maxSize, Math.Max(r.BidSize, r.AskSize));

        using var headerText = Text(_palette.MutedText, 10f, SKTextAlign.Center);
        using var sep = new SKPaint { Color = _palette.GridLine.ToSkColor(), StrokeWidth = 1f, IsAntialias = false };

        // Header.
        for (int i = 0; i < columns.Count; i++)
            canvas.DrawText(descs[i].ShortHeader, (x[i] + x[i + 1]) / 2f, bounds.Top + 14f, headerText);
        canvas.DrawLine(left, bounds.Top + headerH, right, bounds.Top + headerH, sep);

        using var cell = Text(_palette.Text, Math.Clamp(rowH * 0.56f, 8f, 12f), SKTextAlign.Right);
        using var priceText = Text(_palette.Text, Math.Clamp(rowH * 0.56f, 8f, 12f), SKTextAlign.Center);

        float y0 = bounds.Top + headerH;
        for (int rIdx = 0; rIdx < rows.Count; rIdx++)
        {
            var row = rows[rIdx];
            float top = y0 + rIdx * rowH;
            float midY = top + rowH / 2f;

            // Row emphasis: best bid/ask, POC.
            if (row.IsBestBid || row.IsBestAsk)
            {
                var c = (row.IsBestBid ? _palette.BidLiquidity : _palette.AskLiquidity).WithAlpha(40);
                using var hl = new SKPaint { Color = c.ToSkColor() };
                canvas.DrawRect(left, top, totalW, rowH, hl);
            }
            else if (row.IsPoc)
            {
                using var hl = new SKPaint { Color = _palette.Warning.WithAlpha(30).ToSkColor() };
                canvas.DrawRect(left, top, totalW, rowH, hl);
            }

            for (int i = 0; i < columns.Count; i++)
            {
                float cl = x[i], cr = x[i + 1];
                DrawCell(canvas, columns[i], row, cl, cr, top, rowH, midY, maxSize, tickSize, spec, cell, priceText);
            }
        }

        // READ ONLY badge, bottom-right, restrained.
        using var ro = Text(_palette.MutedText, 9f, SKTextAlign.Right);
        canvas.DrawText("READ ONLY", right, bounds.Bottom - 4f, ro);
    }

    private void DrawCell(SKCanvas canvas, DomColumnType type, in DomRow row, float cl, float cr,
        float top, float rowH, float midY, long maxSize, decimal tickSize, InstrumentSpec? spec,
        SKPaint cell, SKPaint priceText)
    {
        float pad = 6f;
        switch (type)
        {
            case DomColumnType.Price:
            {
                decimal price = (spec is null ? row.PriceTicks * tickSize : PriceConverter.ToPrice(spec, row.PriceTicks));
                var col = row.IsBestBid ? _palette.BidLiquidity : row.IsBestAsk ? _palette.AskLiquidity : _palette.Text;
                priceText.Color = col.ToSkColor();
                canvas.DrawText(price.ToString("0.00"), (cl + cr) / 2f, midY + cell.TextSize * 0.35f, priceText);
                priceText.Color = _palette.Text.ToSkColor();
                break;
            }
            case DomColumnType.BidSize:
                if (row.BidSize > 0) DrawSizeCell(canvas, row.BidSize, maxSize, cl, cr, top, rowH, _palette.BidLiquidity, row.IsBidWall, cell);
                break;
            case DomColumnType.AskSize:
                if (row.AskSize > 0) DrawSizeCell(canvas, row.AskSize, maxSize, cl, cr, top, rowH, _palette.AskLiquidity, row.IsAskWall, cell);
                break;
            case DomColumnType.BidCumulative:
                if (row.CumulativeBid > 0) DrawNum(canvas, row.CumulativeBid, cr - pad, midY, _palette.MutedText, cell);
                break;
            case DomColumnType.AskCumulative:
                if (row.CumulativeAsk > 0) DrawNum(canvas, row.CumulativeAsk, cr - pad, midY, _palette.MutedText, cell);
                break;
            case DomColumnType.BidExecuted:
                if (row.TradedAtBid > 0) DrawNum(canvas, row.TradedAtBid, cr - pad, midY, _palette.AskLiquidity, cell);
                break;
            case DomColumnType.AskExecuted:
                if (row.TradedAtAsk > 0) DrawNum(canvas, row.TradedAtAsk, cr - pad, midY, _palette.BidLiquidity, cell);
                break;
            case DomColumnType.Delta:
                if (row.TotalTraded > 0) DrawSigned(canvas, row.Delta, cr - pad, midY, cell);
                break;
            case DomColumnType.BidPullStack:
                DrawPullStack(canvas, row.BidStacked - row.BidPulled, cr - pad, midY, cell);
                break;
            case DomColumnType.AskPullStack:
                DrawPullStack(canvas, row.AskStacked - row.AskPulled, cr - pad, midY, cell);
                break;
            case DomColumnType.BidRefill:
                if (row.BidReplenish > 0) DrawNum(canvas, row.BidReplenish, cr - pad, midY, _palette.MutedText, cell);
                break;
            case DomColumnType.AskRefill:
                if (row.AskReplenish > 0) DrawNum(canvas, row.AskReplenish, cr - pad, midY, _palette.MutedText, cell);
                break;
            case DomColumnType.BidRelative:
                if (row.BidSize > 0) DrawPct(canvas, row.BidSize / (double)maxSize, cr - pad, midY, _palette.MutedText, cell);
                break;
            case DomColumnType.AskRelative:
                if (row.AskSize > 0) DrawPct(canvas, row.AskSize / (double)maxSize, cr - pad, midY, _palette.MutedText, cell);
                break;
            default:
                // MaxTrade / TradeCount / MBO columns: data not available in this snapshot.
                DrawDash(canvas, cr - pad, midY, cell);
                break;
        }
    }

    private void DrawSizeCell(SKCanvas canvas, long size, long maxSize, float cl, float cr, float top, float rowH,
        RgbaColor color, bool wall, SKPaint cell)
    {
        float frac = (float)Math.Clamp(size / (double)maxSize, 0, 1);
        float barW = (cr - cl) * frac;
        using (var bar = new SKPaint { Color = color.WithAlpha(70).ToSkColor() })
            canvas.DrawRect(cr - barW, top + 1, barW, rowH - 2, bar); // bar grows from the price side (right edge)
        if (wall)
            using (var outline = new SKPaint { Color = color.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true })
                canvas.DrawRect(cl + 1, top + 1, cr - cl - 2, rowH - 2, outline);
        DrawNum(canvas, size, cr - 6f, top + rowH / 2f, color, cell);
    }

    private static void DrawNum(SKCanvas canvas, long value, float xRight, float midY, RgbaColor color, SKPaint cell)
    {
        cell.Color = color.ToSkColor();
        canvas.DrawText(value.ToString("N0"), xRight, midY + cell.TextSize * 0.35f, cell);
    }

    private void DrawSigned(SKCanvas canvas, long delta, float xRight, float midY, SKPaint cell)
    {
        cell.Color = (delta >= 0 ? _palette.BidLiquidity : _palette.AskLiquidity).ToSkColor();
        canvas.DrawText((delta >= 0 ? "+" : "") + delta.ToString("N0"), xRight, midY + cell.TextSize * 0.35f, cell);
    }

    private void DrawPullStack(SKCanvas canvas, long net, float xRight, float midY, SKPaint cell)
    {
        if (net == 0) return;
        cell.Color = (net >= 0 ? _palette.BidLiquidity : _palette.AskLiquidity).ToSkColor();
        canvas.DrawText((net >= 0 ? "+" : "") + net.ToString("N0"), xRight, midY + cell.TextSize * 0.35f, cell);
    }

    private static void DrawPct(SKCanvas canvas, double frac, float xRight, float midY, RgbaColor color, SKPaint cell)
    {
        cell.Color = color.ToSkColor();
        canvas.DrawText($"{frac * 100:0}%", xRight, midY + cell.TextSize * 0.35f, cell);
    }

    private void DrawDash(SKCanvas canvas, float xRight, float midY, SKPaint cell)
    {
        cell.Color = _palette.GridLine.ToSkColor();
        canvas.DrawText("–", xRight, midY + cell.TextSize * 0.35f, cell);
    }

    private void DrawCentered(SKCanvas canvas, SKRect bounds, string text, RgbaColor color, float size)
    {
        using var p = Text(color, size, SKTextAlign.Center);
        canvas.DrawText(text, bounds.MidX, bounds.MidY, p);
    }

    private static SKPaint Text(RgbaColor color, float size, SKTextAlign align) => new()
    {
        Color = color.ToSkColor(),
        IsAntialias = true,
        Typeface = SKTypeface.Default,
        TextSize = size,
        TextAlign = align,
    };
}
