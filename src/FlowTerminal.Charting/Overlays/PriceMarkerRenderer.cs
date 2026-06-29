using System.Globalization;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Draws the current-price marker: a thin dashed line at the latest close across the
/// plot and a filled price tag in the right axis gutter. The tag is green when the
/// last bar closed up and light purple when it closed down — never red.
/// </summary>
public sealed class PriceMarkerRenderer
{
    private readonly ChartPalette _palette;

    public PriceMarkerRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    /// <param name="axisX">Right edge of the plot (where the price axis gutter begins).</param>
    public void Render(SKCanvas canvas, ChartViewport vp, long priceTicks, bool bullish, decimal tickSize, float axisX)
    {
        if (priceTicks < vp.MinPriceTicks || priceTicks >= vp.MaxPriceTicks) return;

        float y = vp.PriceToY(priceTicks);
        var color = (bullish ? _palette.BullishCandle : _palette.BearishCandle).ToSkColor();

        using var line = new SKPaint
        {
            Color = color.WithAlpha(170),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 3f }, 0),
        };
        canvas.DrawLine(vp.PlotLeft, y, axisX, y, line);

        string text = (priceTicks * tickSize).ToString("N2", CultureInfo.InvariantCulture);
        using var label = new SKPaint
        {
            Color = _palette.Background.ToSkColor(), // dark text on the coloured price pill
            IsAntialias = true,
            TextSize = 11f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        };
        using var pill = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };

        float textW = label.MeasureText(text);
        float padX = 7f, h = 17f;
        var rect = new SKRect(axisX + 2f, y - h / 2f, axisX + 2f + textW + padX * 2f, y + h / 2f);
        canvas.DrawRoundRect(rect, 3f, 3f, pill);
        canvas.DrawText(text, rect.Left + padX, y + 4f, label);
    }
}
