using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Rich footprint rendering: legible cells with POC, imbalance outlines, zero prints,
/// large trades and a delta footer, in the existing green/purple palette (no thermal),
/// across every display mode; plus end-to-end replay determinism of the footprint hash.
/// </summary>
public class FootprintRichRenderTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);

    private static FootprintBar Reference()
    {
        var f = new Footprint();
        void Ask(long p, long q) => f.AddAt(p, q, AggressorSide.Buy);
        void Bid(long p, long q) => f.AddAt(p, q, AggressorSide.Sell);
        Ask(100, 9); Bid(100, 1);
        Ask(101, 2); Bid(101, 10);
        Ask(102, 8); Bid(102, 3);
        Ask(103, 60); Bid(103, 4);   // large trade + buy imbalance
        Ask(104, 5);                  // interior zero print
        Bid(105, 6);
        Bid(99, 7);
        return FootprintAggregator.Build(f, 100, 105, 99, 104, default, default, true,
            FootprintSettings.Default with { LargeTradeThreshold = 50 });
    }

    [Fact]
    public void Rich_Footprint_Renders_Green_Purple_No_Thermal()
    {
        var bar = Reference();
        // 7 price rows (99..105) over 360px → rowH ≈ 45 (legible); one wide bar.
        var vp = new ChartViewport(520, 360, 97, 107, 0, 1, rightAxisWidth: 64, topPadding: 6, bottomPadding: 30);
        using var bmp = new SKBitmap(520, 360);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        new FootprintRenderer().Render(canvas, vp, new[] { bar }, FootprintSettings.Default with { LargeTradeThreshold = 50 });

        bool green = false, purple = false, thermalRed = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Green > p.Red && p.Green > p.Blue && p.Green > 60) green = true;
            if (p.Blue > p.Green && p.Red > p.Green && p.Blue > 70) purple = true;
            if (p.Red > 180 && p.Green < 90 && p.Blue < 90) thermalRed = true; // bearish red (forbidden)
        }

        Assert.True(green, "expected green ask/buy elements");
        Assert.True(purple, "expected light-purple bid/sell elements");
        Assert.False(thermalRed, "footprint must never use red for bearish/sell");

        var dir = Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, "footprint.png"), d.ToArray());
        }
    }

    [Theory]
    [InlineData(FootprintMode.BidAsk)]
    [InlineData(FootprintMode.Delta)]
    [InlineData(FootprintMode.TotalVolume)]
    [InlineData(FootprintMode.BidOnly)]
    [InlineData(FootprintMode.AskOnly)]
    [InlineData(FootprintMode.DeltaPercent)]
    [InlineData(FootprintMode.TradeCount)]
    [InlineData(FootprintMode.VolumeProfile)]
    public void Every_Mode_Renders_Without_Exception(FootprintMode mode)
    {
        var bar = Reference();
        var vp = new ChartViewport(520, 360, 97, 107, 0, 1, rightAxisWidth: 64, topPadding: 6, bottomPadding: 30);
        using var bmp = new SKBitmap(520, 360);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        new FootprintRenderer().Render(canvas, vp, new[] { bar }, FootprintSettings.Default with { Mode = mode });

        bool drew = false;
        for (int x = 0; x < bmp.Width && !drew; x += 3)
        for (int y = 0; y < bmp.Height && !drew; y += 3)
            if (bmp.GetPixel(x, y).Red + bmp.GetPixel(x, y).Green + bmp.GetPixel(x, y).Blue > 40) drew = true;
        Assert.True(drew, $"mode {mode} should draw something");
    }

    [Fact]
    public void Footprint_Is_Deterministic_Across_Repeated_Synthetic_Replay()
    {
        ulong[] Run()
        {
            var gen = new SyntheticSessionGenerator(1, Nq, new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc),
                new SyntheticOptions { Seed = 7, StartPrice = 20_000m });
            var agg = BarAggregator.Time(TimeSpan.FromSeconds(5));
            var fp = new Footprint();
            var hashes = new List<ulong>();
            for (int i = 0; i < 80_000; i++)
            {
                var e = gen.Next();
                if (e.Type != MarketEventType.Trade) continue;
                fp.AddTrade(e);
                if (agg.AddTrade(e) is { } completed)
                {
                    var bar = FootprintAggregator.Build(fp, completed.OpenTicks, completed.HighTicks,
                        completed.LowTicks, completed.CloseTicks, completed.StartUtc, completed.EndUtc, true);
                    hashes.Add(bar.Hash);
                    fp.Reset();
                }
            }

            return hashes.ToArray();
        }

        var a = Run();
        var b = Run();
        Assert.NotEmpty(a);
        Assert.Equal(a, b); // identical seed + settings ⇒ identical footprint hashes
    }
}
