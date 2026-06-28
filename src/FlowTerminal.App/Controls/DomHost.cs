using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Domain.Instruments;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Read-only depth-of-market ladder rendered on a single Skia canvas (no per-row
/// WPF controls). Observational only: it shows price, bid/ask size, and traded
/// volume. There are no order-entry, working-order, position, or P&amp;L elements.
/// </summary>
public sealed class DomHost : SKElement
{
    private readonly ChartPalette _palette = ChartPalette.Default;
    private IReadOnlyList<DomRow> _rows = Array.Empty<DomRow>();
    private readonly InstrumentSpec _spec = InstrumentRegistry.NQ;

    public void UpdateRows(IReadOnlyList<DomRow> rows)
    {
        _rows = rows;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(_palette.PanelBackground.ToSkColor());
        if (_rows.Count == 0)
        {
            return;
        }

        float h = e.Info.Height;
        float w = e.Info.Width;
        float rowH = Math.Max(12f, h / _rows.Count);
        float mid = w / 2f;

        using var bid = new SKPaint { Color = _palette.BidLiquidity.ToSkColor(), Style = SKPaintStyle.Fill };
        using var ask = new SKPaint { Color = _palette.AskLiquidity.ToSkColor(), Style = SKPaintStyle.Fill };
        using var pocPaint = new SKPaint { Color = _palette.Warning.WithAlpha(40).ToSkColor(), Style = SKPaintStyle.Fill };
        using var text = new SKPaint
        {
            Color = _palette.Text.ToSkColor(),
            IsAntialias = true,
            TextSize = Math.Min(13f, rowH * 0.7f),
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.Default,
        };

        long maxSize = 1;
        foreach (var r in _rows)
        {
            maxSize = Math.Max(maxSize, Math.Max(r.BidSize, r.AskSize));
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            float y = i * rowH;

            if (r.IsPoc)
            {
                canvas.DrawRect(0, y, w, rowH, pocPaint);
            }

            float bidW = mid * (r.BidSize / (float)maxSize);
            float askW = mid * (r.AskSize / (float)maxSize);
            canvas.DrawRect(mid - bidW, y + 1, bidW, rowH - 2, bid);
            canvas.DrawRect(mid, y + 1, askW, rowH - 2, ask);

            decimal price = PriceConverter.ToPrice(_spec, r.PriceTicks);
            canvas.DrawText(price.ToString("0.00"), mid - 30, y + rowH * 0.75f, text);
        }
    }
}
