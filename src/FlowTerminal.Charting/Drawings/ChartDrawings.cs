using System.Globalization;
using SkiaSharp;

namespace FlowTerminal.Charting.Drawings;

/// <summary>The drawing tools available on the chart. <see cref="Select"/> draws nothing (pan/zoom).</summary>
public enum DrawingTool
{
    Select,
    Trendline,
    HorizontalLine,
    Ray,
    Rectangle,
    Fibonacci,
    Measure,
    Erase,
}

/// <summary>
/// A drawing anchor in data space: a time (mapped to a bar X) and an integer-tick
/// price (mapped to Y). Anchoring in data space keeps drawings pinned to the market
/// as the chart pans and zooms.
/// </summary>
public readonly record struct AnchorPoint(DateTime Time, long PriceTicks);

/// <summary>A user drawing: a tool plus its anchor points (B is unused for single-point tools).</summary>
public sealed class ChartDrawing
{
    public ChartDrawing(DrawingTool tool, AnchorPoint a, AnchorPoint b)
    {
        Tool = tool;
        A = a;
        B = b;
    }

    public DrawingTool Tool { get; }
    public AnchorPoint A { get; set; }
    public AnchorPoint B { get; set; }
}

/// <summary>
/// Renders chart drawings through the shared viewport so they track price and time.
/// Time→X mapping is supplied by the host (it knows the bar list); price→Y comes from
/// the viewport. Lines use cyan, Fibonacci amber, measure cyan — never red for normal
/// drawings. Pure drawing; no interaction state.
/// </summary>
public sealed class DrawingRenderer
{
    private static readonly double[] FibLevels = { 0, 0.236, 0.382, 0.5, 0.618, 0.786, 1.0 };

    private readonly ChartPalette _palette;

    public DrawingRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, ChartViewport vp, ChartDrawing d, Func<DateTime, float> timeToX, decimal tickSize, bool preview = false)
    {
        float xa = timeToX(d.A.Time), ya = vp.PriceToY(d.A.PriceTicks);
        float xb = timeToX(d.B.Time), yb = vp.PriceToY(d.B.PriceTicks);
        byte alpha = (byte)(preview ? 150 : 255);

        switch (d.Tool)
        {
            case DrawingTool.Trendline:
                canvas.DrawLine(xa, ya, xb, yb, Stroke(_palette.SelectedObject, 1.7f, alpha));
                Handle(canvas, xa, ya); Handle(canvas, xb, yb);
                break;

            case DrawingTool.Ray:
            {
                float ex = vp.PlotRight, ey = yb;
                if (xb != xa) ey = ya + (yb - ya) * (ex - xa) / (xb - xa);
                using var p = Stroke(_palette.SelectedObject, 1.7f, alpha);
                canvas.DrawLine(xa, ya, ex, ey, p);
                Handle(canvas, xa, ya);
                break;
            }

            case DrawingTool.HorizontalLine:
            {
                using var p = Stroke(_palette.SelectedObject, 1.4f, alpha);
                canvas.DrawLine(vp.PlotLeft, ya, vp.PlotRight, ya, p);
                Label(canvas, vp.PlotRight - 4, ya, Price(d.A.PriceTicks, tickSize), _palette.SelectedObject, right: true);
                break;
            }

            case DrawingTool.Rectangle:
            {
                var rect = new SKRect(Math.Min(xa, xb), Math.Min(ya, yb), Math.Max(xa, xb), Math.Max(ya, yb));
                using var fill = new SKPaint { Color = _palette.SelectedObject.WithAlpha((byte)(preview ? 18 : 28)).ToSkColor(), IsAntialias = true };
                canvas.DrawRect(rect, fill);
                canvas.DrawRect(rect, Stroke(_palette.SelectedObject, 1.3f, alpha));
                break;
            }

            case DrawingTool.Fibonacci:
            {
                float left = Math.Min(xa, xb), right = Math.Max(xa, xb);
                foreach (var lvl in FibLevels)
                {
                    float y = (float)(ya + (yb - ya) * lvl);
                    long ticks = (long)Math.Round(d.A.PriceTicks + (d.B.PriceTicks - d.A.PriceTicks) * lvl);
                    canvas.DrawLine(left, y, right, y, Stroke(_palette.Warning, 1f, (byte)(alpha * 0.8)));
                    Label(canvas, left + 3, y, $"{lvl:0.###}  {Price(ticks, tickSize)}", _palette.Warning, right: false);
                }

                canvas.DrawLine(xa, ya, xb, yb, Stroke(_palette.Warning, 1.2f, (byte)(alpha * 0.6)));
                break;
            }

            case DrawingTool.Measure:
            {
                canvas.DrawLine(xa, ya, xb, yb, Stroke(_palette.SelectedObject, 1.6f, alpha));
                Handle(canvas, xa, ya); Handle(canvas, xb, yb);
                decimal diff = (d.B.PriceTicks - d.A.PriceTicks) * tickSize;
                decimal pa = d.A.PriceTicks * tickSize;
                decimal pct = pa != 0 ? diff / pa * 100m : 0m;
                string sign = diff >= 0 ? "+" : "−";
                string text = $"{sign}{Math.Abs(diff).ToString("N2", CultureInfo.InvariantCulture)}  ({sign}{Math.Abs(pct).ToString("N2", CultureInfo.InvariantCulture)}%)";
                Label(canvas, (xa + xb) / 2f, Math.Min(ya, yb) - 8, text, _palette.SelectedObject, right: false, center: true);
                break;
            }
        }
    }

    private SKPaint Stroke(RgbaColor c, float w, byte alpha) => new()
    {
        Color = c.WithAlpha(alpha).ToSkColor(),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = w,
        IsAntialias = true,
    };

    private void Handle(SKCanvas canvas, float x, float y)
    {
        using var fill = new SKPaint { Color = _palette.Background.ToSkColor(), IsAntialias = true };
        using var ring = new SKPaint { Color = _palette.SelectedObject.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f, IsAntialias = true };
        canvas.DrawCircle(x, y, 3.2f, fill);
        canvas.DrawCircle(x, y, 3.2f, ring);
    }

    private void Label(SKCanvas canvas, float x, float y, string text, RgbaColor color, bool right, bool center = false)
    {
        using var paint = new SKPaint
        {
            Color = color.ToSkColor(),
            IsAntialias = true,
            TextSize = 10.5f,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextAlign = center ? SKTextAlign.Center : right ? SKTextAlign.Right : SKTextAlign.Left,
        };
        canvas.DrawText(text, x, y - 3, paint);
    }

    private static string Price(long ticks, decimal tickSize) =>
        (ticks * tickSize).ToString("N2", CultureInfo.InvariantCulture);
}
