using FlowTerminal.Analytics.BigTrades;
using FlowTerminal.Charting;
using FlowTerminal.Charting.BigTrades;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

public class BigTradeRenderTests
{
    private static readonly DateTime T = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Tr(double sec, long price, long qty, AggressorSide a, bool supplied = true) =>
        MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME", T.AddSeconds(sec), T.AddSeconds(sec), price, qty, a,
            flags: supplied ? MarketEventFlags.AggressorSupplied : MarketEventFlags.None);

    /// <summary>A scene with buy/sell/unknown bubbles, a sweep, and varied sizes.</summary>
    private static IReadOnlyList<BigTradeGroup> Scene()
    {
        var s = new BigTradeSettings
        {
            Mode = ThresholdMode.Fixed, FixedBuyThreshold = 20, FixedSellThreshold = 20, AbsoluteFloor = 5,
            Aggregation = AggregationMode.Sweep, SweepMinLevels = 3, SweepMinVolume = 30, TimeWindowMs = 300,
            MaxTickDistance = 1, MinSamples = 1, ShowUnknown = true,
        }.Validate();
        var d = new BigTradeDetector(s);

        d.OnTrade(Tr(0.5, 1000, 30, AggressorSide.Buy), 999, 1001, true);      // medium buy
        d.OnTrade(Tr(2.0, 980, 120, AggressorSide.Sell), 979, 981, true);      // large sell
        d.OnTrade(Tr(3.0, 1010, 60, AggressorSide.Buy), 1009, 1011, true);     // buy
        // A buy sweep across 3 levels.
        d.OnTrade(Tr(4.0, 1020, 25, AggressorSide.Buy), 1019, 1020, true);
        d.OnTrade(Tr(4.05, 1021, 25, AggressorSide.Buy), 1020, 1021, true);
        d.OnTrade(Tr(4.10, 1022, 25, AggressorSide.Buy), 1021, 1022, true);
        // Unknown-side big trade (invalid book).
        var unk = MarketEvent.Trade(1, RootSymbol.NQ, "NQZ5", "CME", T.AddSeconds(5), T.AddSeconds(5), 995, 50, AggressorSide.Unknown);
        d.OnTrade(unk, 994, 996, bookValid: false);
        d.Flush();

        return d.Snapshot(T.AddSeconds(6));
    }

    [Fact]
    public void Renders_Green_Purple_Neutral_No_Red()
    {
        var groups = Scene();
        Assert.NotEmpty(groups);

        using var bmp = new SKBitmap(640, 420);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.PanelBackground.ToSkColor());
        new BigTradeRenderer().Render(canvas, new SKRect(0, 0, 640, 420), groups,
            T, T.AddSeconds(6), 970, 1030, 0.25m);

        bool green = false, purple = false, gray = false, red = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Green > p.Red + 25 && p.Green > p.Blue + 10 && p.Green > 90) green = true;
            if (p.Blue > p.Green + 15 && p.Red > p.Green && p.Blue > 110) purple = true;
            if (Math.Abs(p.Red - p.Green) < 20 && Math.Abs(p.Green - p.Blue) < 20 && p.Red is > 70 and < 170) gray = true;
            if (p.Red > 180 && p.Green < 90 && p.Blue < 90) red = true;
        }

        Assert.True(green, "expected green buy bubbles");
        Assert.True(purple, "expected light-purple sell bubbles");
        Assert.True(gray, "expected neutral unknown bubble");
        Assert.False(red, "Big Trades must not use red");
    }

    [Fact]
    public void Candle_Overlay_Maps_Groups_To_Bars_And_Renders()
    {
        var groups = Scene();
        Assert.NotEmpty(groups);

        // Build a minute-bar series spanning the scene and a real chart viewport.
        var bars = new List<FlowTerminal.Analytics.Bars.Bar>();
        for (int i = 0; i < 8; i++)
            bars.Add(new FlowTerminal.Analytics.Bars.Bar(
                FlowTerminal.Analytics.Bars.BarKind.Time, T.AddSeconds(i), T.AddSeconds(i + 1),
                1000, 1030, 970, 1005, 100, 60, 40, 10));

        var vp = new FlowTerminal.Charting.ChartViewport(700, 460, 965, 1035, 0, bars.Count);

        int Containing(DateTime t)
        {
            int ans = 0;
            for (int i = 0; i < bars.Count; i++) if (bars[i].StartUtc <= t) ans = i;
            return ans;
        }
        float MapX(DateTime t) => vp.BarCenterX(Containing(t));

        using var bmp = new SKBitmap(700, 460);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        new BigTradeRenderer().RenderMapped(canvas,
            new SKRect(vp.PlotLeft, vp.PlotTop, vp.PlotRight, vp.PlotBottom),
            groups, MapX, vp.PriceToY, vp.MinPriceTicks, vp.MaxPriceTicks);

        bool green = false, purple = false, red = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Green > p.Red + 25 && p.Green > p.Blue + 10 && p.Green > 90) green = true;
            if (p.Blue > p.Green + 15 && p.Red > p.Green && p.Blue > 110) purple = true;
            if (p.Red > 180 && p.Green < 90 && p.Blue < 90) red = true;
        }
        Assert.True(green && purple, "expected green and purple bubbles on the candle overlay");
        Assert.False(red, "Big Trades must not use red");
    }

    [Fact]
    public void Empty_Groups_Draw_Nothing_Without_Throwing()
    {
        using var bmp = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bmp);
        new BigTradeRenderer().Render(canvas, new SKRect(0, 0, 200, 200),
            System.Array.Empty<BigTradeGroup>(), T, T.AddSeconds(1), 100, 110, 0.25m);
        // No exception is the assertion.
    }

    [Fact]
    public void Candle_Preview()
    {
        var dir = System.Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var groups = Scene();

        // Varied candles trending up through the scene's price range.
        long[] closes = { 990, 996, 1001, 998, 1005, 1012, 1018, 1024 };
        var bars = new List<FlowTerminal.Analytics.Bars.Bar>();
        long prev = 985;
        for (int i = 0; i < closes.Length; i++)
        {
            long o = prev, c = closes[i];
            long hi = Math.Max(o, c) + 6, lo = Math.Min(o, c) - 6;
            bars.Add(new FlowTerminal.Analytics.Bars.Bar(
                FlowTerminal.Analytics.Bars.BarKind.Time, T.AddSeconds(i), T.AddSeconds(i + 1),
                o, hi, lo, c, 100, 60, 40, 10));
            prev = c;
        }

        const int w = 760, h = 460;
        var vp = new FlowTerminal.Charting.ChartViewport(w, h, 965, 1035, 0, bars.Count);
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        new FlowTerminal.Charting.CandlestickRenderer().Render(canvas, vp, bars);

        int Containing(DateTime t) { int a = 0; for (int i = 0; i < bars.Count; i++) if (bars[i].StartUtc <= t) a = i; return a; }
        float MapX(DateTime t)
        {
            int i = Containing(t);
            var start = bars[i].StartUtc;
            var end = i + 1 < bars.Count ? bars[i + 1].StartUtc : start.AddSeconds(1);
            double frac = Math.Clamp((t - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
            return vp.BarCenterX(i) + (float)(frac - 0.5) * vp.BarSlotWidth;
        }

        new BigTradeRenderer().RenderMapped(canvas,
            new SKRect(vp.PlotLeft, vp.PlotTop, vp.PlotRight, vp.PlotBottom),
            groups, MapX, vp.PriceToY, vp.MinPriceTicks, vp.MaxPriceTicks);

        System.IO.Directory.CreateDirectory(dir);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "bigtrades_candles.png"), data.ToArray());
    }

    [Fact]
    public void FormatQty_Is_Compact()
    {
        Assert.Equal("950", BigTradeRenderer.FormatQty(950));
        Assert.Equal("1.2k", BigTradeRenderer.FormatQty(1200));
        Assert.Equal("18k", BigTradeRenderer.FormatQty(18000));
    }

    [Fact]
    public void Preview_Montage()
    {
        var dir = System.Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var groups = Scene();

        var styles = new[] { BigTradeBubbleStyle.Solid, BigTradeBubbleStyle.Ring, BigTradeBubbleStyle.OutlineQuantity };
        const int w = 520, h = 420;
        using var bmp = new SKBitmap(w * styles.Length, h);
        using var canvas = new SKCanvas(bmp);
        using var lbl = new SKPaint { Color = ChartPalette.Default.Text.ToSkColor(), TextSize = 13, IsAntialias = true };

        for (int i = 0; i < styles.Length; i++)
        {
            using var panel = new SKBitmap(w, h - 18);
            using (var pc = new SKCanvas(panel))
            {
                pc.Clear(ChartPalette.Default.PanelBackground.ToSkColor());
                new BigTradeRenderer().Render(pc, new SKRect(8, 8, w - 8, h - 26), groups,
                    T, T.AddSeconds(6), 970, 1030, 0.25m,
                    new BigTradeVisualSettings { Style = styles[i] });
            }
            canvas.DrawBitmap(panel, i * w, 18);
            canvas.DrawText(styles[i].ToString(), i * w + 8, 13, lbl);
        }

        System.IO.Directory.CreateDirectory(dir);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "bigtrades.png"), data.ToArray());
    }
}
