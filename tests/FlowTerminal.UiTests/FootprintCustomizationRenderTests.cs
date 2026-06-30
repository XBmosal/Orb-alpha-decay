using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using FlowTerminal.Domain.Events;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Renders every built-in preset and every compatible data-mode × visual-layout
/// combination, asserting each draws in the existing green/purple palette with no
/// red/thermal colour, and dumps a montage of the distinct presets for eyeballing.
/// </summary>
public class FootprintCustomizationRenderTests
{
    private static FootprintBar Reference()
    {
        var f = new Footprint();
        void Ask(long p, long q) => f.AddAt(p, q, AggressorSide.Buy);
        void Bid(long p, long q) => f.AddAt(p, q, AggressorSide.Sell);
        Ask(100, 9); Bid(100, 1);
        Ask(101, 2); Bid(101, 10);
        Ask(102, 8); Bid(102, 3);
        Ask(103, 60); Bid(103, 4);
        Ask(104, 5);
        Bid(105, 6);
        Bid(99, 7);
        return FootprintAggregator.Build(f, 100, 105, 99, 104, default, default, true,
            FootprintSettings.Default with { LargeTradeThreshold = 50 });
    }

    private static (bool green, bool purple, bool red, bool any) Render(FootprintSettings s)
    {
        var bar = Reference();
        var vp = new ChartViewport(520, 360, 97, 107, 0, 1, rightAxisWidth: 64, topPadding: 6, bottomPadding: 30);
        using var bmp = new SKBitmap(520, 360);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        new FootprintRenderer().Render(canvas, vp, new[] { bar }, s);

        bool green = false, purple = false, red = false, any = false;
        for (int x = 0; x < bmp.Width; x += 2)
        for (int y = 0; y < bmp.Height; y += 2)
        {
            var px = bmp.GetPixel(x, y);
            if (px.Red + px.Green + px.Blue > 40) any = true;
            if (px.Green > px.Red && px.Green > px.Blue && px.Green > 55) green = true;
            if (px.Blue > px.Green && px.Red > px.Green && px.Blue > 65) purple = true;
            if (px.Red > 180 && px.Green < 90 && px.Blue < 90) red = true;
        }

        return (green, purple, red, any);
    }

    [Fact]
    public void Every_Builtin_Preset_Renders_Green_Purple_No_Red()
    {
        var dir = Environment.GetEnvironmentVariable("FT_PREVIEW_DIR");
        foreach (var preset in FootprintPresetRegistry.BuiltIns)
        {
            var (green, purple, red, any) = Render(preset.Settings);
            Assert.True(any, $"preset '{preset.Name}' drew nothing");
            Assert.True(green || purple, $"preset '{preset.Name}' had no green/purple");
            Assert.False(red, $"preset '{preset.Name}' used red");
        }

        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            DumpMontage(Path.Combine(dir, "footprint_presets.png"));
        }
    }

    [Fact]
    public void Every_Compatible_Mode_Layout_Combination_Renders()
    {
        foreach (FootprintMode mode in Enum.GetValues<FootprintMode>())
        foreach (var layout in FootprintCompatibility.AllowedLayouts(mode))
        {
            var (_, _, red, any) = Render(FootprintSettings.Default with { Mode = mode, VisualLayout = layout });
            Assert.True(any, $"{mode}/{layout} drew nothing");
            Assert.False(red, $"{mode}/{layout} used red");
        }
    }

    [Fact]
    public void Distinct_Presets_Produce_Distinct_Renders()
    {
        // Bid×Ask split text, zero-centred delta histogram, and a volume profile should
        // not produce identical pixels.
        long Sig(FootprintSettings s)
        {
            var bar = Reference();
            var vp = new ChartViewport(520, 360, 97, 107, 0, 1, rightAxisWidth: 64, topPadding: 6, bottomPadding: 30);
            using var bmp = new SKBitmap(520, 360);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Black);
            new FootprintRenderer().Render(canvas, vp, new[] { bar }, s);
            long acc = 0;
            for (int x = 0; x < bmp.Width; x += 4)
            for (int y = 0; y < bmp.Height; y += 4)
            {
                var px = bmp.GetPixel(x, y);
                acc = acc * 31 + px.Red + 7 * px.Green + 13 * px.Blue + x;
            }
            return acc;
        }

        long a = Sig(FootprintPresetRegistry.ByName("Classic Bid×Ask")!.Settings);
        long b = Sig(FootprintPresetRegistry.ByName("Delta Profile")!.Settings);
        long c = Sig(FootprintPresetRegistry.ByName("Volume Profile")!.Settings);
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(b, c);
    }

    private static void DumpMontage(string path)
    {
        var presets = FootprintPresetRegistry.BuiltIns;
        const int cols = 3, cw = 360, ch = 240;
        int rows = (presets.Count + cols - 1) / cols;
        using var bmp = new SKBitmap(cols * cw, rows * ch);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(ChartPalette.Default.Background.ToSkColor());
        using var label = new SKPaint { Color = ChartPalette.Default.MutedText.ToSkColor(), TextSize = 12, IsAntialias = true };

        for (int i = 0; i < presets.Count; i++)
        {
            int cxi = i % cols, cyi = i / cols;
            float ox = cxi * cw, oy = cyi * ch;
            var bar = Reference();
            var vp = new ChartViewport(cw, ch - 24, 97, 107, 0, 1, rightAxisWidth: 40, topPadding: 6, bottomPadding: 26);
            canvas.Save();
            canvas.Translate(ox, oy + 18);
            new FootprintRenderer().Render(canvas, vp, new[] { bar }, presets[i].Settings);
            canvas.Restore();
            canvas.DrawText(presets[i].Name, ox + 6, oy + 14, label);
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
