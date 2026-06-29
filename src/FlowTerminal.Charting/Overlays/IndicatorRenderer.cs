using SkiaSharp;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Draws the standard technical indicators in the existing Flow Terminal style: price
/// overlays (moving-average line, Bollinger/Donchian/Keltner envelopes) on the candle
/// plot, and oscillator sub-panes (RSI, MACD, ADX, …) in reserved strips below the
/// candles. It uses only existing palette colours — green/light-purple for directional
/// elements (e.g. +DI/−DI, MACD histogram), neutral text/grid tones for everything
/// else — and introduces no new theme.
/// </summary>
public sealed class IndicatorRenderer
{
    private readonly ChartPalette _p;

    public IndicatorRenderer(ChartPalette? palette = null) => _p = palette ?? ChartPalette.Default;

    public float PaneHeight => 96f;

    /// <summary>Total vertical strip (px) the active oscillator panes need below the candles.</summary>
    public float ReservedPaneHeight(IndicatorRenderData data) => data.Panes.Count * PaneHeight;

    // ── Price overlays (caller clips to the plot) ───────────────────────────

    public void RenderOverlays(SKCanvas canvas, ChartViewport vp, IndicatorRenderData data)
    {
        foreach (var band in data.Bands) DrawBand(canvas, vp, band);
        foreach (var line in data.OverlayLines) DrawPriceLine(canvas, vp, line.Values, line.Color, line.Width);
        DrawOverlayLegend(canvas, vp, data);
    }

    private void DrawPriceLine(SKCanvas canvas, ChartViewport vp, IReadOnlyList<double> values, RgbaColor color, float width)
    {
        using var paint = new SKPaint { Color = color.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
        using var path = new SKPath();
        bool started = false;
        for (int i = 0; i < values.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            double v = values[i];
            if (double.IsNaN(v)) { started = false; continue; }
            float x = vp.BarCenterX(i);
            float y = vp.PriceToY((long)Math.Round(v));
            if (!started) { path.MoveTo(x, y); started = true; }
            else path.LineTo(x, y);
        }

        canvas.DrawPath(path, paint);
    }

    private void DrawBand(SKCanvas canvas, ChartViewport vp, IndicatorBand band)
    {
        using var fill = new SKPaint { Color = band.Color.WithAlpha(28).ToSkColor(), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var top = new SKPath();
        using var bottom = new SKPath();
        bool started = false;
        var pts = new List<(float x, float yu, float yl)>();
        for (int i = 0; i < band.Upper.Count; i++)
        {
            if (!vp.IsBarVisible(i)) continue;
            if (double.IsNaN(band.Upper[i]) || double.IsNaN(band.Lower[i])) { started = false; continue; }
            float x = vp.BarCenterX(i);
            float yu = vp.PriceToY((long)Math.Round(band.Upper[i]));
            float yl = vp.PriceToY((long)Math.Round(band.Lower[i]));
            pts.Add((x, yu, yl));
            if (!started) { top.MoveTo(x, yu); started = true; } else top.LineTo(x, yu);
        }

        // Close the fill polygon along the lower band (reversed) and paint it faintly.
        if (pts.Count > 1)
        {
            using var area = new SKPath();
            area.MoveTo(pts[0].x, pts[0].yu);
            for (int i = 1; i < pts.Count; i++) area.LineTo(pts[i].x, pts[i].yu);
            for (int i = pts.Count - 1; i >= 0; i--) area.LineTo(pts[i].x, pts[i].yl);
            area.Close();
            canvas.DrawPath(area, fill);
        }

        DrawPriceLine(canvas, vp, band.Upper, band.Color, 1.1f);
        DrawPriceLine(canvas, vp, band.Lower, band.Color, 1.1f);
        DrawPriceLine(canvas, vp, band.Mid, band.Color.WithAlpha(150), 1.0f);
    }

    private void DrawOverlayLegend(SKCanvas canvas, ChartViewport vp, IndicatorRenderData data)
    {
        if (data.OverlayLines.Count == 0 && data.Bands.Count == 0) return;
        using var text = new SKPaint { Color = _p.MutedText.ToSkColor(), IsAntialias = true, TextSize = 10.5f, Typeface = SKTypeface.Default };
        float y = vp.PlotTop + 12f;
        foreach (var l in data.OverlayLines) { DrawLegendRow(canvas, vp.PlotLeft + 8f, ref y, l.Label, l.Color, text); }
        foreach (var b in data.Bands) { DrawLegendRow(canvas, vp.PlotLeft + 8f, ref y, b.Label, b.Color, text); }
    }

    private static void DrawLegendRow(SKCanvas canvas, float x, ref float y, string label, RgbaColor color, SKPaint text)
    {
        using var swatch = new SKPaint { Color = color.ToSkColor(), IsAntialias = true };
        canvas.DrawRect(x, y - 7f, 9f, 3f, swatch);
        canvas.DrawText(label, x + 14f, y, text);
        y += 14f;
    }

    // ── Oscillator panes ────────────────────────────────────────────────────

    public void RenderPane(SKCanvas canvas, ChartViewport vp, SKRect rect, OscillatorPane pane)
    {
        // Seamless inset: same chart background, a subtle top separator.
        using (var bg = new SKPaint { Color = _p.Background.ToSkColor(), Style = SKPaintStyle.Fill })
            canvas.DrawRect(rect, bg);
        using (var sep = new SKPaint { Color = _p.GridLine.ToSkColor(), StrokeWidth = 1f, IsAntialias = false })
            canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, sep);

        float padY = 8f;
        float top = rect.Top + padY, bottom = rect.Bottom - padY;

        (double min, double max) = PaneRange(vp, pane);
        if (max - min < 1e-9) { min -= 1; max += 1; }

        float Y(double v)
        {
            double f = (Math.Clamp(v, min, max) - min) / (max - min);
            return (float)(bottom - f * (bottom - top));
        }

        // Reference levels (dashed, neutral grid tone).
        using (var refPaint = new SKPaint
        {
            Color = _p.GridLine.ToSkColor(), StrokeWidth = 1f, IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0),
        })
        {
            foreach (var lvl in pane.ReferenceLevels)
            {
                if (lvl < min || lvl > max) continue;
                float y = Y(lvl);
                canvas.DrawLine(rect.Left, y, rect.Right, y, refPaint);
            }
        }

        // Histogram (green above zero, light purple below) — the green/purple identity.
        if (pane.Histogram is { } hist)
        {
            float baseY = Y(0);
            float bw = Math.Max(1f, vp.BarSlotWidth * 0.6f);
            for (int i = 0; i < hist.Count; i++)
            {
                if (!vp.IsBarVisible(i) || double.IsNaN(hist[i])) continue;
                float x = vp.BarCenterX(i);
                float y = Y(hist[i]);
                var col = hist[i] >= 0 ? _p.PositiveDelta : _p.NegativeDelta;
                using var hp = new SKPaint { Color = col.WithAlpha(170).ToSkColor(), IsAntialias = false };
                float yt = Math.Min(baseY, y), yb = Math.Max(baseY, y);
                canvas.DrawRect(x - bw / 2f, yt, bw, Math.Max(1f, yb - yt), hp);
            }
        }

        // Lines.
        foreach (var line in pane.Lines)
        {
            using var paint = new SKPaint { Color = line.Color.ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = line.Width, IsAntialias = true };
            using var path = new SKPath();
            bool started = false;
            for (int i = 0; i < line.Values.Count; i++)
            {
                if (!vp.IsBarVisible(i)) continue;
                double v = line.Values[i];
                if (double.IsNaN(v)) { started = false; continue; }
                float x = vp.BarCenterX(i), y = Y(v);
                if (!started) { path.MoveTo(x, y); started = true; } else path.LineTo(x, y);
            }

            canvas.DrawPath(path, paint);
        }

        // Label + latest value.
        using var label = new SKPaint { Color = _p.MutedText.ToSkColor(), IsAntialias = true, TextSize = 10.5f, Typeface = SKTypeface.Default };
        canvas.DrawText(pane.Label, rect.Left + 8f, rect.Top + 13f, label);
        double latest = LatestValue(pane);
        if (!double.IsNaN(latest))
        {
            using var val = new SKPaint { Color = _p.Text.ToSkColor(), IsAntialias = true, TextSize = 10.5f, Typeface = SKTypeface.Default, TextAlign = SKTextAlign.Right };
            canvas.DrawText(latest.ToString("0.##"), rect.Right - 8f, rect.Top + 13f, val);
        }
    }

    private static (double min, double max) PaneRange(ChartViewport vp, OscillatorPane pane)
    {
        if (pane.FixedMin is { } fmin && pane.FixedMax is { } fmax) return (fmin, fmax);

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        void Acc(IReadOnlyList<double> s)
        {
            for (int i = 0; i < s.Count; i++)
            {
                if (!vp.IsBarVisible(i) || double.IsNaN(s[i])) continue;
                min = Math.Min(min, s[i]); max = Math.Max(max, s[i]);
            }
        }

        foreach (var l in pane.Lines) Acc(l.Values);
        if (pane.Histogram is { } h) Acc(h);
        foreach (var lvl in pane.ReferenceLevels) { min = Math.Min(min, lvl); max = Math.Max(max, lvl); }

        if (double.IsInfinity(min) || double.IsInfinity(max)) return (pane.FixedMin ?? -1, pane.FixedMax ?? 1);
        if (pane.FixedMin is { } fm) min = fm;
        if (pane.FixedMax is { } fx) max = fx;
        if (pane.ZeroCentered)
        {
            double m = Math.Max(Math.Abs(min), Math.Abs(max));
            return (-m, m);
        }

        double pad = (max - min) * 0.08;
        return (min - pad, max + pad);
    }

    private static double LatestValue(OscillatorPane pane)
    {
        var s = pane.Lines.Count > 0 ? pane.Lines[0].Values : null;
        if (s is null) return double.NaN;
        for (int i = s.Count - 1; i >= 0; i--) if (!double.IsNaN(s[i])) return s[i];
        return double.NaN;
    }
}
