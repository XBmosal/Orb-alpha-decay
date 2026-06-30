namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Render-ready output for the standard technical indicators. All value arrays are
/// aligned 1:1 with the chart's bar list (index = bar position); entries are
/// <see cref="double.NaN"/> where the indicator has not warmed up, so the renderer
/// simply skips them. Prices are in ticks. Colours come from the existing palette —
/// this model carries no new colours.
/// </summary>
public sealed record IndicatorRenderData(
    IReadOnlyList<IndicatorLine> OverlayLines,
    IReadOnlyList<IndicatorBand> Bands,
    IReadOnlyList<OscillatorPane> Panes)
{
    public static IndicatorRenderData Empty { get; } =
        new(Array.Empty<IndicatorLine>(), Array.Empty<IndicatorBand>(), Array.Empty<OscillatorPane>());

    public bool IsEmpty => OverlayLines.Count == 0 && Bands.Count == 0 && Panes.Count == 0;
}

/// <summary>A single overlay line (in price ticks) drawn on the price chart.</summary>
public sealed record IndicatorLine(string Label, RgbaColor Color, IReadOnlyList<double> Values, float Width = 1.6f);

/// <summary>An overlay envelope (upper/mid/lower price-tick lines with a faint fill).</summary>
public sealed record IndicatorBand(
    string Label, RgbaColor Color,
    IReadOnlyList<double> Upper, IReadOnlyList<double> Mid, IReadOnlyList<double> Lower);

/// <summary>
/// One oscillator sub-pane (RSI, MACD, ADX, …). The pane reserves a horizontal strip
/// below the candles; its lines/histogram are aligned to the same bar indices.
/// <see cref="FixedMin"/>/<see cref="FixedMax"/> pin the vertical scale (e.g. 0–100);
/// when null the scale auto-fits the visible values (symmetric if <see cref="ZeroCentered"/>).
/// </summary>
public sealed record OscillatorPane(
    string Label,
    IReadOnlyList<IndicatorLine> Lines,
    IReadOnlyList<double>? Histogram,
    double? FixedMin,
    double? FixedMax,
    IReadOnlyList<double> ReferenceLevels,
    bool ZeroCentered);
