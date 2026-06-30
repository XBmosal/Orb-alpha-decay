using FlowTerminal.Analytics.Bars;
using SkiaSharp;

namespace FlowTerminal.Charting;

/// <summary>
/// Renders candlesticks onto an <see cref="SKCanvas"/> using reusable paint
/// objects (no per-candle allocations, no per-candle WPF controls). Only the
/// visible bar range is drawn.
///
/// Mandatory color rule: bullish (close ≥ open) candles are green (#22C55E);
/// bearish candles are light purple (#C4A7FF). Red is never used here.
/// </summary>
public sealed class CandlestickRenderer : IDisposable
{
    private readonly SKPaint _bullBody;
    private readonly SKPaint _bearBody;
    private readonly SKPaint _bullWick;
    private readonly SKPaint _bearWick;
    private readonly SKPaint _background;
    private readonly SKPaint _grid;

    public CandlestickRenderer(ChartPalette? palette = null)
    {
        Palette = palette ?? ChartPalette.Default;
        _bullBody = Fill(Palette.BullishCandle);
        _bearBody = Fill(Palette.BearishCandle);
        _bullWick = Stroke(Palette.BullishWick, 1.2f);
        _bearWick = Stroke(Palette.BearishWick, 1.2f);
        _background = Fill(Palette.Background);
        _grid = Stroke(Palette.GridLine, 1f);
    }

    public ChartPalette Palette { get; }

    /// <summary>The color a candle body should use given its open/close — the mandated rule.</summary>
    public static RgbaColor BodyColor(ChartPalette palette, long openTicks, long closeTicks) =>
        closeTicks >= openTicks ? palette.BullishCandle : palette.BearishCandle;

    public static bool IsBullish(long openTicks, long closeTicks) => closeTicks >= openTicks;

    public void Render(SKCanvas canvas, ChartViewport viewport, IReadOnlyList<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);

        canvas.Clear(Palette.Background.ToSkColor());
        canvas.DrawRect(viewport.PlotLeft, viewport.PlotTop, viewport.PlotWidth, viewport.PlotHeight, _background);

        float bodyWidth = Math.Max(1f, viewport.BarSlotWidth * 0.62f);

        for (int i = 0; i < bars.Count; i++)
        {
            if (!viewport.IsBarVisible(i))
            {
                continue;
            }

            var bar = bars[i];
            bool bull = IsBullish(bar.OpenTicks, bar.CloseTicks);
            float cx = viewport.BarCenterX(i);

            float yHigh = viewport.PriceToY(bar.HighTicks);
            float yLow = viewport.PriceToY(bar.LowTicks);
            float yOpen = viewport.PriceToY(bar.OpenTicks);
            float yClose = viewport.PriceToY(bar.CloseTicks);

            // Wick
            canvas.DrawLine(cx, yHigh, cx, yLow, bull ? _bullWick : _bearWick);

            // Body (at least 1px tall for doji)
            float top = Math.Min(yOpen, yClose);
            float bottom = Math.Max(yOpen, yClose);
            if (bottom - top < 1f)
            {
                bottom = top + 1f;
            }

            canvas.DrawRect(cx - bodyWidth / 2f, top, bodyWidth, bottom - top, bull ? _bullBody : _bearBody);
        }
    }

    private static SKPaint Fill(RgbaColor c) => new() { Color = c.ToSkColor(), Style = SKPaintStyle.Fill, IsAntialias = true };

    private static SKPaint Stroke(RgbaColor c, float w) => new()
    {
        Color = c.ToSkColor(),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = w,
        IsAntialias = true,
    };

    public void Dispose()
    {
        _bullBody.Dispose();
        _bearBody.Dispose();
        _bullWick.Dispose();
        _bearWick.Dispose();
        _background.Dispose();
        _grid.Dispose();
    }
}
