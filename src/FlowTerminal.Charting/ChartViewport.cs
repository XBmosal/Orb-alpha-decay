namespace FlowTerminal.Charting;

/// <summary>
/// Maps between data space (price in ticks, bar index) and pixel space for a chart
/// panel. Pure and allocation-free so it is unit-testable without any rendering
/// backend. Y grows downward (screen convention): the highest price maps to the
/// top of the plot area.
/// </summary>
public sealed class ChartViewport
{
    public ChartViewport(
        float width, float height,
        long minPriceTicks, long maxPriceTicks,
        int firstBarIndex, int visibleBarCount,
        float leftPadding = 0f, float rightAxisWidth = 64f, float topPadding = 8f, float bottomPadding = 8f)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport must have positive size.");
        }

        if (maxPriceTicks <= minPriceTicks)
        {
            throw new ArgumentException("maxPriceTicks must exceed minPriceTicks.");
        }

        if (visibleBarCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleBarCount));
        }

        Width = width;
        Height = height;
        MinPriceTicks = minPriceTicks;
        MaxPriceTicks = maxPriceTicks;
        FirstBarIndex = firstBarIndex;
        VisibleBarCount = visibleBarCount;
        LeftPadding = leftPadding;
        RightAxisWidth = rightAxisWidth;
        TopPadding = topPadding;
        BottomPadding = bottomPadding;
    }

    public float Width { get; }
    public float Height { get; }
    public long MinPriceTicks { get; }
    public long MaxPriceTicks { get; }
    public int FirstBarIndex { get; }
    public int VisibleBarCount { get; }
    public float LeftPadding { get; }
    public float RightAxisWidth { get; }
    public float TopPadding { get; }
    public float BottomPadding { get; }

    public float PlotLeft => LeftPadding;
    public float PlotRight => Width - RightAxisWidth;
    public float PlotTop => TopPadding;
    public float PlotBottom => Height - BottomPadding;
    public float PlotWidth => Math.Max(0f, PlotRight - PlotLeft);
    public float PlotHeight => Math.Max(0f, PlotBottom - PlotTop);

    /// <summary>Horizontal pixel pitch allocated to each bar slot.</summary>
    public float BarSlotWidth => PlotWidth / VisibleBarCount;

    /// <summary>Y pixel for a price (ticks). Highest price → top.</summary>
    public float PriceToY(long priceTicks)
    {
        double frac = (double)(priceTicks - MinPriceTicks) / (MaxPriceTicks - MinPriceTicks);
        return (float)(PlotBottom - frac * PlotHeight);
    }

    /// <summary>Inverse of <see cref="PriceToY"/>.</summary>
    public long YToPrice(float y)
    {
        double frac = (PlotBottom - y) / PlotHeight;
        return MinPriceTicks + (long)Math.Round(frac * (MaxPriceTicks - MinPriceTicks));
    }

    /// <summary>Center X pixel for a bar by absolute index.</summary>
    public float BarCenterX(int barIndex)
    {
        int slot = barIndex - FirstBarIndex;
        return PlotLeft + (slot + 0.5f) * BarSlotWidth;
    }

    public bool IsBarVisible(int barIndex) =>
        barIndex >= FirstBarIndex && barIndex < FirstBarIndex + VisibleBarCount;
}
