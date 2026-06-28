namespace FlowTerminal.Charting;

/// <summary>
/// A plain, dependency-free RGBA color. The palette is expressed with this type so
/// color logic and tests do not require the native SkiaSharp runtime; a separate
/// extension converts to <c>SKColor</c> at the rendering boundary.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Parses "#RRGGBB" or "#RRGGBBAA".</summary>
    public static RgbaColor FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        var s = hex.TrimStart('#');
        if (s.Length is not (6 or 8))
        {
            throw new FormatException($"Expected #RRGGBB or #RRGGBBAA, got '{hex}'.");
        }

        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
        return new RgbaColor(r, g, b, a);
    }

    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public RgbaColor WithAlpha(byte alpha) => this with { A = alpha };
}
