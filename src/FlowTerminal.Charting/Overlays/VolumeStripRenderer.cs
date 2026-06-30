using FlowTerminal.Analytics.Bars;
using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Draws a per-bar volume histogram along the bottom of the plot as a study overlay
/// (the "Volume" indicator). Each bar is colored by its direction — green for
/// bullish, light purple for bearish — and scaled to the heaviest bar in view. The
/// strip is translucent so the price series above it stays readable.
/// </summary>
public sealed class VolumeStripRenderer
{
    private readonly ChartPalette _palette;

    public VolumeStripRenderer(ChartPalette? palette = null) => _palette = palette ?? ChartPalette.Default;

    public void Render(SKCanvas canvas, ChartViewport vp, IReadOnlyList<Bar> bars)
    {
        long maxVol = 1;
        for (int i = 0; i < bars.Count; i++)
        {
            if (vp.IsBarVisible(i)) maxVol = Math.Max(maxVol, bars[i].Volume);
        }

        float band = vp.PlotHeight * 0.16f;
        float baseY = vp.PlotBottom;
        float barW = Math.Max(1f, vp.BarSlotWidth * 0.6f);

        using var bull = new SKPaint { Color = _palette.BullishCandle.WithAlpha(90).ToSkColor(), IsAntialias = false };
        using var bear = new SKPaint { Color = _palette.BearishCandle.WithAlpha(90).ToSkColor(), IsAntialias = false };

        for (int i = 0; i < bars.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            var bar = bars[i];
            float h = band * (bar.Volume / (float)maxVol);
            if (h < 0.5f) continue;
            float cx = vp.BarCenterX(i);
            var paint = bar.CloseTicks >= bar.OpenTicks ? bull : bear;
            canvas.DrawRect(cx - barW / 2f, baseY - h, barW, h, paint);
        }
    }
}
