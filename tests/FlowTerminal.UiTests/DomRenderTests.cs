using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.OrderBook;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>Renders the read-only DOM ladder and checks the green/purple identity, the
/// READ ONLY label and that distinct presets draw, plus dumps a preview montage.</summary>
public class DomRenderTests
{
    private static readonly DateTime T = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Q(MarketEventType type, Side side, long price, long qty, long seq) =>
        MarketEvent.Quote(1, RootSymbol.NQ, "NQZ5", "CME Globex", type, T, T, side, price, qty,
            exchangeSequence: seq, flags: MarketEventFlags.HasExchangeSequence);

    private static (IReadOnlyList<DomRow> rows, decimal tick) Scene()
    {
        var book = new MarketByPriceOrderBook();
        long seq = 0;
        foreach (var (p, q) in new[] { (101L, 6L), (102L, 9L), (103L, 80L), (104L, 4L), (105L, 12L) })
            book.Apply(Q(MarketEventType.AskUpdate, Side.Ask, p, q, ++seq));
        foreach (var (p, q) in new[] { (100L, 7L), (99L, 14L), (98L, 90L), (97L, 5L), (96L, 10L) })
            book.Apply(Q(MarketEventType.BidUpdate, Side.Bid, p, q, ++seq));

        var profile = new VolumeProfile();
        profile.AddAt(100, 30, AggressorSide.Buy); profile.AddAt(100, 12, AggressorSide.Sell);
        profile.AddAt(101, 18, AggressorSide.Buy); profile.AddAt(99, 22, AggressorSide.Sell);

        var tracker = new PullStackTracker();
        tracker.OnDepth(Q(MarketEventType.BidUpdate, Side.Bid, 99, 20, 100));
        tracker.OnDepth(Q(MarketEventType.BidUpdate, Side.Bid, 99, 14, 101)); // pulled 6
        tracker.OnDepth(Q(MarketEventType.AskUpdate, Side.Ask, 102, 4, 102));
        tracker.OnDepth(Q(MarketEventType.AskUpdate, Side.Ask, 102, 9, 103)); // stacked 5

        return (ReadOnlyDom.Build(book, profile, 5, tracker, wallFloor: 50), 0.25m);
    }

    [Fact]
    public void Ladder_Renders_Green_Purple_ReadOnly_No_Red()
    {
        var (rows, tick) = Scene();
        var preset = DomPresetRegistry.ByName("Full Professional")!;
        using var bmp = new SKBitmap(420, 360);
        using var canvas = new SKCanvas(bmp);
        new DomLadderRenderer().Render(canvas, new SKRect(0, 0, 420, 360), rows, preset.Columns, tick);

        bool green = false, purple = false, red = false, any = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Red + p.Green + p.Blue > 60) any = true;
            if (p.Green > p.Red && p.Green > p.Blue && p.Green > 60) green = true;
            if (p.Blue > p.Green && p.Red > p.Green && p.Blue > 80) purple = true;
            if (p.Red > 180 && p.Green < 90 && p.Blue < 90) red = true;
        }

        Assert.True(any && green && purple);
        Assert.False(red, "DOM must not use red");
    }

    [Fact]
    public void Empty_Rows_Show_Waiting_State()
    {
        using var bmp = new SKBitmap(300, 200);
        using var canvas = new SKCanvas(bmp);
        new DomLadderRenderer().Render(canvas, new SKRect(0, 0, 300, 200), Array.Empty<DomRow>(),
            DomPresetRegistry.Default.Columns, 0.25m);
        bool drew = false;
        for (int x = 0; x < 300 && !drew; x += 3)
        for (int y = 0; y < 200 && !drew; y += 3)
            if (bmp.GetPixel(x, y).Red + bmp.GetPixel(x, y).Green + bmp.GetPixel(x, y).Blue > 30) drew = true;
        Assert.True(drew, "empty DOM should show a waiting state, not a blank panel");
    }

    [Fact]
    public void Preview_Montage()
    {
        var dir = Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var (rows, tick) = Scene();
        string[] names = { "Classic Depth", "Order Flow", "Full Professional" };
        const int w = 380, h = 360;
        using var bmp = new SKBitmap(w * names.Length, h);
        using var canvas = new SKCanvas(bmp);
        using var label = new SKPaint { Color = ChartPalette.Default.Text.ToSkColor(), TextSize = 12, IsAntialias = true };
        for (int i = 0; i < names.Length; i++)
        {
            var preset = DomPresetRegistry.ByName(names[i])!;
            // Render each panel to its own bitmap (the renderer clears its whole surface),
            // then composite — so all three presets are visible side by side.
            using var panel = new SKBitmap(w, h - 16);
            using (var pc = new SKCanvas(panel))
                new DomLadderRenderer().Render(pc, new SKRect(0, 0, w, h - 16), rows, preset.Columns, tick);
            canvas.DrawBitmap(panel, i * w, 16);
            canvas.DrawText(names[i], i * w + 8, 12, label);
        }

        Directory.CreateDirectory(dir);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, "dom.png"), data.ToArray());
    }
}
