using System.Globalization;
using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Domain.Events;
using SkiaSharp;

namespace FlowTerminal.Charting.BigTrades;

/// <summary>Bubble appearance for the Big Trades renderer (style/size/labels; visual only).</summary>
public enum BigTradeBubbleStyle { Solid, Ring, Soft, Dot, OutlineQuantity }

/// <summary>Nonlinear bubble-size mapping (linear is intentionally not offered).</summary>
public enum BigTradeScaleMode { Sqrt, Log }

public sealed record BigTradeVisualSettings
{
    public BigTradeBubbleStyle Style { get; init; } = BigTradeBubbleStyle.Solid;
    public BigTradeScaleMode ScaleMode { get; init; } = BigTradeScaleMode.Sqrt;
    public float MinRadius { get; init; } = 3.5f;
    public float MaxRadius { get; init; } = 22f;
    public byte FillOpacity { get; init; } = 200;
    public bool ShowLabels { get; init; } = true;
    public float MinRadiusForLabel { get; init; } = 11f;
    public bool ShowSweepCapsule { get; init; } = true;

    public static BigTradeVisualSettings Default { get; } = new();
}

/// <summary>
/// Renders <see cref="BigTradeGroup"/> bubbles at their executed price (Y) and time (X)
/// on a Skia panel. Aggressive buys are Flow Terminal green, sells light purple, unknown
/// neutral gray; estimated-aggressor groups get a dashed outline, sweeps a stronger
/// outline and an optional price capsule. Bubble <em>area</em> tracks volume via a
/// nonlinear (√/log) map so large prints don't dominate and ordinary qualifying prints
/// stay visible. Z-order is deterministic (small first, big and sweeps last). Strictly
/// observational — bubbles are markers with no interaction that could place an order.
/// </summary>
public sealed class BigTradeRenderer
{
    private readonly ChartPalette _palette;

    public BigTradeRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    /// <summary>
    /// Draws the groups within the time window [<paramref name="t0"/>,<paramref name="t1"/>]
    /// and price window [<paramref name="minPriceTicks"/>,<paramref name="maxPriceTicks"/>].
    /// The caller owns the surface/clear; this only paints bubbles inside <paramref name="bounds"/>.
    /// </summary>
    public void Render(
        SKCanvas canvas, SKRect bounds, IReadOnlyList<BigTradeGroup> groups,
        DateTime t0, DateTime t1, long minPriceTicks, long maxPriceTicks, decimal tickSize,
        BigTradeVisualSettings? visual = null)
    {
        if (groups.Count == 0 || maxPriceTicks <= minPriceTicks) return;
        double totalSec = Math.Max(1e-6, (t1 - t0).TotalSeconds);
        long span = maxPriceTicks - minPriceTicks;
        float X(DateTime t) => bounds.Left + (float)((t - t0).TotalSeconds / totalSec) * bounds.Width;
        float Y(long price) => bounds.Bottom - (price - minPriceTicks) / (float)span * bounds.Height;
        RenderMapped(canvas, bounds, groups, X, Y, minPriceTicks, maxPriceTicks, visual);
    }

    /// <summary>
    /// Renders bubbles using caller-supplied coordinate maps, so a bar-indexed candle chart
    /// and a continuous-time heatmap can share one bubble implementation. <paramref name="x"/>
    /// maps a trade time to a pixel X (e.g. via the bar it falls in); <paramref name="y"/>
    /// maps a price (ticks) to a pixel Y. Groups outside the price window or the horizontal
    /// plot are culled.
    /// </summary>
    public void RenderMapped(
        SKCanvas canvas, SKRect plot, IReadOnlyList<BigTradeGroup> groups,
        Func<DateTime, float> x, Func<long, float> y, long minPriceTicks, long maxPriceTicks,
        BigTradeVisualSettings? visual = null)
    {
        if (groups.Count == 0 || maxPriceTicks <= minPriceTicks) return;
        var v = visual ?? BigTradeVisualSettings.Default;

        // Reference magnitude for size normalization: the largest visible total.
        long refMax = 1;
        foreach (var g in groups) refMax = Math.Max(refMax, g.TotalQuantity);

        // Deterministic z-order: smallest first, then sweeps, then very large; ties by id.
        var ordered = new List<BigTradeGroup>(groups);
        ordered.Sort(static (a, b) =>
        {
            int c = a.TotalQuantity.CompareTo(b.TotalQuantity);
            if (c != 0) return c;
            c = (a.IsSweep ? 1 : 0).CompareTo(b.IsSweep ? 1 : 0);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        using var label = new SKPaint
        {
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        };

        foreach (var g in ordered)
        {
            if (g.PriceTicks < minPriceTicks || g.PriceTicks >= maxPriceTicks) continue;

            float r = Radius(g.TotalQuantity, refMax, v);
            float cx = x(g.EndUtc);
            if (cx < plot.Left - r || cx > plot.Right + r) continue; // off the horizontal plot
            float cy = y(g.PriceTicks);
            var baseColor = ColorFor(g.Side);

            // Optional sweep capsule spanning the executed price range.
            if (v.ShowSweepCapsule && g.IsSweep && g.MaxPriceTicks > g.MinPriceTicks)
            {
                float top = y(g.MaxPriceTicks), bot = y(g.MinPriceTicks);
                using var cap = new SKPaint { Color = baseColor.WithAlpha(48).ToSkColor(), IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(cx - r * 0.5f, top, cx + r * 0.5f, bot), r * 0.5f, r * 0.5f, cap);
            }

            DrawBubble(canvas, cx, cy, r, baseColor, g, v);

            if (v.ShowLabels && r >= v.MinRadiusForLabel)
            {
                label.TextSize = Math.Clamp(r * 0.9f, 8f, 13f);
                label.Color = LabelColor(baseColor).ToSkColor();
                canvas.DrawText(FormatQty(g.TotalQuantity), cx, cy + label.TextSize * 0.35f, label);
            }
        }
    }

    private void DrawBubble(SKCanvas canvas, float cx, float cy, float r, RgbaColor baseColor, in BigTradeGroup g, BigTradeVisualSettings v)
    {
        byte fillA = v.FillOpacity;
        var style = v.Style;

        if (style is BigTradeBubbleStyle.Solid or BigTradeBubbleStyle.Soft or BigTradeBubbleStyle.Dot)
        {
            byte a = style == BigTradeBubbleStyle.Soft ? (byte)(fillA * 0.5f) : fillA;
            DrawSphere(canvas, cx, cy, r, baseColor, a);
        }
        else if (style == BigTradeBubbleStyle.Ring)
        {
            using var ring = new SKPaint { Color = baseColor.WithAlpha(fillA).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, r * 0.22f), IsAntialias = true };
            canvas.DrawCircle(cx, cy, r, ring);
        }
        else // OutlineQuantity
        {
            using var faint = new SKPaint { Color = baseColor.WithAlpha((byte)(fillA * 0.22f)).ToSkColor(), IsAntialias = true };
            canvas.DrawCircle(cx, cy, r, faint);
        }

        // Outline: readability over candles/heatmap. Estimated → dashed; sweep / very large → bolder.
        float strokeW = g.IsSweep ? 2.0f : 1.1f;
        var outlineColor = g.IsEstimated ? baseColor.WithAlpha(220) : Darken(baseColor, 0.4).WithAlpha(190);
        using var outline = new SKPaint
        {
            Color = outlineColor.ToSkColor(),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            IsAntialias = true,
            PathEffect = g.IsEstimated ? SKPathEffect.CreateDash(new[] { 3f, 2.4f }, 0) : null,
        };
        canvas.DrawCircle(cx, cy, r, outline);
        outline.PathEffect?.Dispose();
    }

    private void DrawSphere(SKCanvas canvas, float cx, float cy, float r, RgbaColor baseColor, byte alpha)
    {
        var bc = baseColor.WithAlpha(alpha).ToSkColor();
        var light = Lerp(bc, new SKColor(255, 255, 255, alpha), 0.5);
        var dark = Lerp(bc, new SKColor(0, 0, 0, alpha), 0.42);
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx - r * 0.32f, cy - r * 0.32f), r * 1.25f,
            new[] { light, bc, dark }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, paint);
    }

    private float Radius(long quantity, long refMax, BigTradeVisualSettings v)
    {
        double norm = v.ScaleMode == BigTradeScaleMode.Log
            ? Math.Log(1 + quantity) / Math.Log(1 + refMax)
            : Math.Sqrt(quantity / (double)refMax);
        norm = Math.Clamp(norm, 0, 1);
        return (float)(v.MinRadius + (v.MaxRadius - v.MinRadius) * norm);
    }

    private RgbaColor ColorFor(AggressorSide side) => side switch
    {
        AggressorSide.Buy => _palette.BidLiquidity,
        AggressorSide.Sell => _palette.AskLiquidity,
        _ => _palette.NeutralVolume,
    };

    private RgbaColor LabelColor(RgbaColor bubble)
    {
        // High-contrast label: near-black on the bright bid/ask fills, light on gray.
        double luma = 0.299 * bubble.R + 0.587 * bubble.G + 0.114 * bubble.B;
        return luma > 140 ? new RgbaColor(0x0A, 0x0C, 0x11, 0xFF) : _palette.Text;
    }

    /// <summary>Compact contract count: 950, 1.2k, 18k.</summary>
    public static string FormatQty(long qty)
    {
        if (qty < 1000) return qty.ToString(CultureInfo.InvariantCulture);
        if (qty < 10_000) return (qty / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
        return (qty / 1000).ToString(CultureInfo.InvariantCulture) + "k";
    }

    private static RgbaColor Darken(RgbaColor c, double f) => new(
        (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)), c.A);

    private static SKColor Lerp(SKColor a, SKColor b, double f) => new(
        (byte)(a.Red + (b.Red - a.Red) * f),
        (byte)(a.Green + (b.Green - a.Green) * f),
        (byte)(a.Blue + (b.Blue - a.Blue) * f),
        (byte)(a.Alpha + (b.Alpha - a.Alpha) * f));
}
