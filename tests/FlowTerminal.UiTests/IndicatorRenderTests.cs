using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Tests the technical-indicator series engine (alignment, warm-up, determinism) and
/// the renderer (draws in the existing green/purple style, never a thermal palette).
/// </summary>
public class IndicatorRenderTests
{
    private static List<Bar> SyntheticBars(int n)
    {
        // A trend up, a reversal, then chop — enough to exercise +DI/−DI and a MACD
        // histogram that crosses zero (so both green and purple appear).
        var bars = new List<Bar>(n);
        double p = 1000;
        var t = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            double drift = i < n / 2 ? 3.0 : -3.0;
            double wobble = Math.Sin(i * 0.4) * 2.0;
            double open = p;
            p += drift + wobble;
            double close = p;
            long hi = (long)Math.Round(Math.Max(open, close) + 2);
            long lo = (long)Math.Round(Math.Min(open, close) - 2);
            bars.Add(new Bar(BarKind.Time, t, t.AddMinutes(1),
                (long)Math.Round(open), hi, lo, (long)Math.Round(close), 100, 55, 45, 20));
            t = t.AddMinutes(1);
        }

        return bars;
    }

    private static SortedSet<string> Enabled(params string[] codes) => new(codes);

    [Fact]
    public void Engine_Aligns_Series_Warms_Up_And_Is_Deterministic()
    {
        var bars = SyntheticBars(120);
        var engine = new IndicatorSeriesEngine();
        var data1 = engine.Compute(bars, Enabled("MA", "BB", "RSI", "MACD", "ADX"));
        var data2 = engine.Compute(bars, Enabled("MA", "BB", "RSI", "MACD", "ADX"));

        Assert.Single(data1.OverlayLines);          // MA
        Assert.Single(data1.Bands);                 // BB
        Assert.Equal(3, data1.Panes.Count);         // RSI, MACD, ADX

        var ma = data1.OverlayLines[0].Values;
        Assert.Equal(bars.Count, ma.Count);         // aligned 1:1
        Assert.True(double.IsNaN(ma[0]));           // warm-up: nothing before period
        Assert.False(double.IsNaN(ma[^1]));         // warmed up by the end

        // Determinism: identical inputs → identical outputs.
        var ma2 = data2.OverlayLines[0].Values;
        for (int i = 0; i < ma.Count; i++)
            Assert.True(ma[i].Equals(ma2[i]) || (double.IsNaN(ma[i]) && double.IsNaN(ma2[i])));
    }

    [Fact]
    public void Empty_When_Nothing_Enabled()
    {
        var data = new IndicatorSeriesEngine().Compute(SyntheticBars(50), new SortedSet<string>());
        Assert.True(data.IsEmpty);
    }

    [Fact]
    public void Oscillator_Panes_Are_Capped_At_Three()
    {
        var data = new IndicatorSeriesEngine().Compute(
            SyntheticBars(120), Enabled("RSI", "MACD", "ADX", "ATR", "CCI", "STOCH"));
        Assert.Equal(3, data.Panes.Count);
    }

    [Fact]
    public void Renders_In_Green_And_Purple_Without_Thermal_Palette()
    {
        var bars = SyntheticBars(140);
        var engine = new IndicatorSeriesEngine();
        var renderer = new IndicatorRenderer();
        var data = engine.Compute(bars, Enabled("MA", "BB", "ADX", "MACD"));

        long lo = long.MaxValue, hi = long.MinValue;
        foreach (var b in bars) { lo = Math.Min(lo, b.LowTicks); hi = Math.Max(hi, b.HighTicks); }

        const float w = 900, h = 520, axis = 22;
        float reserved = renderer.ReservedPaneHeight(data);
        var vp = new ChartViewport(w, h, lo - 4, hi + 4, 0, bars.Count,
            rightAxisWidth: 64f, topPadding: 8f, bottomPadding: axis + reserved);

        using var bmp = new SKBitmap((int)w, (int)h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        renderer.RenderOverlays(canvas, vp, data);

        float paneTop = vp.PlotBottom, paneH = reserved / data.Panes.Count;
        for (int i = 0; i < data.Panes.Count; i++)
        {
            var rect = new SKRect(vp.PlotLeft, paneTop + i * paneH, vp.PlotRight, paneTop + (i + 1) * paneH);
            renderer.RenderPane(canvas, vp, rect, data.Panes[i]);
        }

        bool green = false, purple = false, thermal = false, anyLine = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var px = bmp.GetPixel(x, y);
            if (px.Green > px.Red && px.Green > px.Blue && px.Green > 70) green = true;
            if (px.Blue > px.Green && px.Red > px.Green && px.Blue > 90) purple = true;
            if (px.Red > 180 && px.Green > 120 && px.Green < 200 && px.Blue < 60) thermal = true;
            if (px.Red + px.Green + px.Blue > 120) anyLine = true;
        }

        Assert.True(anyLine, "indicators should draw something");
        Assert.True(green, "+DI / positive MACD histogram should render green");
        Assert.True(purple, "−DI / negative MACD histogram should render light purple");
        Assert.False(thermal, "indicators must not use a thermal/orange palette");

        var dir = Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, "indicators.png"), d.ToArray());
        }
    }
}
