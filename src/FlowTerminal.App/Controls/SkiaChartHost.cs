using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FlowTerminal.Analytics.Bars;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Overlays;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// A single SkiaSharp-backed candlestick chart control with study overlays. The
/// entire chart is drawn on one canvas — there is NO WPF control per candle. The
/// latest bar/overlay snapshot is supplied from the (off-thread) feed and the
/// control repaints on a coalesced timer, so market processing is never tied to the
/// visual frame rate. Which overlays draw is controlled by the shared StudyState.
///
/// Interaction (all view transforms — no data is modified, no trading controls):
///   • Drag in the plot area      → scroll back/forward through history.
///   • Drag the right price axis   → zoom price (drag up = taller candles).
///   • Drag the bottom time axis   → zoom time (drag right = fewer, wider candles).
/// </summary>
public sealed class SkiaChartHost : SKElement
{
    private const float RightAxisWidth = 64f;
    private const float BottomAxisHeight = 22f;
    private const float TopPadding = 8f;

    private readonly CandlestickRenderer _renderer = new();
    private readonly ChartOverlayRenderer _overlays = new();
    private readonly FootprintRenderer _footprint = new();
    private IReadOnlyList<Bar> _bars = Array.Empty<Bar>();
    private ChartOverlays _overlayData = ChartOverlays.Empty;
    private StudyState? _studies;

    /// <summary>Tick size for axis price labels (NQ/ES = 0.25). Set from the instrument spec.</summary>
    public decimal TickSize { get; set; } = 0.25m;

    // View transform state.
    private int _visibleBars = 60;    // horizontal zoom (time)
    private double _priceZoom = 1.0;   // vertical zoom (price); >1 = taller candles
    private int _panBars;              // bars scrolled back from the live right edge

    private enum DragMode { None, Pan, PriceZoom, TimeZoom }

    private DragMode _drag = DragMode.None;
    private Point _dragStart;
    private float _lastBarSlotWidth = 1f;
    private int _dragStartPan;
    private int _dragStartVisible;
    private double _dragStartZoom;

    public SkiaChartHost()
    {
        ClipToBounds = true;
    }

    public void Attach(StudyState studies) => _studies = studies;

    public void UpdateFrame(IReadOnlyList<Bar> bars, ChartOverlays overlays)
    {
        _bars = bars;
        _overlayData = overlays;
        InvalidateVisual();
    }

    // ── Mouse interaction ───────────────────────────────────────────────────

    private DragMode RegionAt(Point p) =>
        p.X >= ActualWidth - RightAxisWidth ? DragMode.PriceZoom
        : p.Y >= ActualHeight - BottomAxisHeight ? DragMode.TimeZoom
        : DragMode.Pan;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragStart = e.GetPosition(this);
        _drag = RegionAt(_dragStart);
        _dragStartPan = _panBars;
        _dragStartVisible = _visibleBars;
        _dragStartZoom = _priceZoom;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragMode.None)
        {
            return;
        }

        var pos = e.GetPosition(this);
        switch (_drag)
        {
            case DragMode.Pan:
            {
                double dx = pos.X - _dragStart.X;
                int barsDelta = (int)Math.Round(dx / Math.Max(1f, _lastBarSlotWidth));
                int visible = Math.Min(_visibleBars, _bars.Count);
                int maxPan = Math.Max(0, _bars.Count - visible);
                _panBars = Math.Clamp(_dragStartPan + barsDelta, 0, maxPan);
                break;
            }

            case DragMode.PriceZoom:
            {
                // Drag up → zoom in (taller candles); drag down → zoom out.
                double dy = _dragStart.Y - pos.Y;
                _priceZoom = Math.Clamp(_dragStartZoom * Math.Exp(dy * 0.005), 0.2, 12.0);
                break;
            }

            case DragMode.TimeZoom:
            {
                // Drag right → fewer, wider candles; drag left → more candles.
                double dx = pos.X - _dragStart.X;
                int target = (int)Math.Round(_dragStartVisible * Math.Exp(-dx * 0.005));
                _visibleBars = Math.Clamp(target, 10, 1000);
                break;
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag != DragMode.None)
        {
            _drag = DragMode.None;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width;
        float h = e.Info.Height;
        canvas.Clear(_renderer.Palette.Background.ToSkColor());

        if (_bars.Count == 0 || w <= 0 || h <= 0)
        {
            return;
        }

        // Footprint mode replaces the candle view with its own wide bid×ask columns.
        if (_studies is not null && _studies.IsEnabled("FP") && _overlayData.Footprint.Count > 0)
        {
            _footprint.Render(canvas, new SKRect(0, 0, w, h), _overlayData.Footprint, TickSize);
            return;
        }

        int visible = Math.Min(_visibleBars, _bars.Count);
        int maxPan = Math.Max(0, _bars.Count - visible);
        if (_panBars > maxPan)
        {
            _panBars = maxPan; // history shrank (eviction) — keep the offset in range
        }

        int first = _bars.Count - visible - _panBars;

        // Auto-fit the price range to the visible bars, then apply the vertical zoom
        // about the mid price so dragging the price axis lengthens/shortens candles.
        long lo = long.MaxValue, hi = long.MinValue;
        for (int i = first; i < first + visible; i++)
        {
            lo = Math.Min(lo, _bars[i].LowTicks);
            hi = Math.Max(hi, _bars[i].HighTicks);
        }

        long pad = Math.Max(1, (hi - lo) / 20);
        double center = (lo + hi) / 2.0;
        double half = ((hi - lo) / 2.0 + pad) / _priceZoom;
        long min = (long)Math.Floor(center - half);
        long max = (long)Math.Ceiling(center + half);
        if (max <= min) max = min + 1;

        // If a volume profile is enabled, reserve a band on the right so the histogram
        // sits in its own column and the candles stop before it (no overlap).
        float totalRight = w - RightAxisWidth;
        bool profileOn = _studies is not null &&
            (_studies.IsEnabled("VBP") || _studies.IsEnabled("BAC") || _studies.IsEnabled("DP"));
        float profileBand = profileOn ? Math.Min(totalRight * 0.18f, 200f) : 0f;

        var viewport = new ChartViewport(
            w, h, min, max, first, visible,
            leftPadding: 0f, rightAxisWidth: RightAxisWidth + profileBand,
            topPadding: TopPadding, bottomPadding: BottomAxisHeight);
        _lastBarSlotWidth = viewport.BarSlotWidth;

        // Clip to the candles + profile band so price-zoomed bars do not bleed into the
        // axis gutters, while still letting the profile draw in its reserved band.
        var plot = new SKRect(viewport.PlotLeft, viewport.PlotTop, totalRight, viewport.PlotBottom);
        canvas.Save();
        canvas.ClipRect(plot);
        _renderer.Render(canvas, viewport, _bars);
        if (_studies is { Enabled.Count: > 0 } studies)
        {
            _overlays.Render(canvas, viewport, _overlayData, studies.Enabled, profileBand);
        }

        canvas.Restore();

        DrawPriceAxis(canvas, viewport, totalRight);
        DrawTimeAxis(canvas, viewport, first, visible);

        if (_panBars > 0)
        {
            DrawHistoryBadge(canvas, viewport.PlotRight);
        }
    }

    private void DrawPriceAxis(SKCanvas canvas, ChartViewport vp, float axisX)
    {
        using var grid = new SKPaint { Color = _renderer.Palette.GridLine.WithAlpha(110).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = TextPaint(10f);
        const int lines = 6;
        long span = vp.MaxPriceTicks - vp.MinPriceTicks;
        for (int i = 0; i <= lines; i++)
        {
            long priceTicks = vp.MinPriceTicks + span * i / lines;
            float y = vp.PriceToY(priceTicks);
            canvas.DrawLine(vp.PlotLeft, y, axisX, y, grid);
            decimal price = priceTicks * TickSize;
            canvas.DrawText(price.ToString("N2", CultureInfo.InvariantCulture), axisX + 6, y + 3.5f, text);
        }
    }

    private void DrawTimeAxis(SKCanvas canvas, ChartViewport vp, int first, int visible)
    {
        using var grid = new SKPaint { Color = _renderer.Palette.GridLine.WithAlpha(110).ToSkColor(), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = TextPaint(10f);
        float axisTop = vp.PlotBottom;
        canvas.DrawLine(vp.PlotLeft, axisTop, vp.PlotRight, axisTop, grid);

        // Choose a bar step that keeps labels ~80px apart.
        int step = Math.Max(1, (int)Math.Ceiling(visible / Math.Max(1f, vp.PlotWidth / 80f)));
        for (int i = first; i < first + visible; i++)
        {
            if ((i - first) % step != 0) continue;
            float x = vp.BarCenterX(i);
            canvas.DrawLine(x, axisTop, x, axisTop + 4, grid);
            string label = _bars[i].StartUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            float tw = text.MeasureText(label);
            canvas.DrawText(label, x - tw / 2f, axisTop + 16f, text);
        }
    }

    private void DrawHistoryBadge(SKCanvas canvas, float plotRight)
    {
        using var bg = new SKPaint { Color = new SKColor(0x0E, 0x0F, 0x12, 0xCC), IsAntialias = true };
        using var fg = new SKPaint { Color = new SKColor(0xC4, 0xA7, 0xFF), IsAntialias = true, TextSize = 12 };
        const string text = "◂ history — drag right edge to resume live";
        float tw = fg.MeasureText(text);
        var rect = new SKRect(plotRight - tw - 24, 8, plotRight - 8, 28);
        canvas.DrawRoundRect(rect, 4, 4, bg);
        canvas.DrawText(text, rect.Left + 8, rect.Bottom - 6, fg);
    }

    private SKPaint TextPaint(float size) => new()
    {
        Color = _renderer.Palette.MutedText.ToSkColor(),
        IsAntialias = true,
        Typeface = SKTypeface.Default,
        TextSize = size,
    };
}
