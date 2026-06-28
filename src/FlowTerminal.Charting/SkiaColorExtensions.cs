using SkiaSharp;

namespace FlowTerminal.Charting;

/// <summary>Bridges the dependency-free palette to SkiaSharp at the rendering boundary.</summary>
public static class SkiaColorExtensions
{
    public static SKColor ToSkColor(this RgbaColor c) => new(c.R, c.G, c.B, c.A);
}
