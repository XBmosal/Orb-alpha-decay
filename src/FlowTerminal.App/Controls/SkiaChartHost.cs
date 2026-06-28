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
/// The view follows the live right edge by default. Click-and-drag scrolls the
/// visible window back through history (drag right → older bars); dragging back to
/// the right edge re-attaches to live. This is purely a view transform — no data is
/// modified and there are no trading controls of any kind.
/// </summary>
public sealed class SkiaChartHost : SKElement
{
    private readonly CandlestickRenderer _renderer = new();
    private readonly ChartOverlayRenderer _overlays = new();
    private readonly FootprintRenderer _footprint = new();
    private IReadOnlyList<Bar> _bars = Array.Empty<Bar>();
    private ChartOverlays _overlayData = ChartOverlays.Empty;
    private StudyState? _studies;
    private readonly int _maxVisibleBars = 120;

    /// <summary>Tick size for axis price labels (NQ/ES = 0.25). Set from the instrument spec.</summary>
    public decimal TickSize { get; set; } = 0.25m;

    // Pan state: number of bars the view is scrolled back from the live right edge.
    // 0 means "follow live". Mouse drag adjusts it; it is clamped to the history.
    private int _panBars;
    private float _lastBarSlotWidth = 1f;
    private bool _dragging;
    private double _dragStartX;
    private int _dragStartPan;

    public SkiaChartHost()
    {
        Cursor = Cursors.Hand; // signal that the chart is draggable
        ClipToBounds = true;
    }

    public void Attach(StudyState studies) => _studies = studies;

    public void UpdateFrame(IReadOnlyList<Bar> bars, ChartOverlays overlays)
    {
        _bars = bars;
        _overlayData = overlays;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartPan = _panBars;
        CaptureMouse();
        Cursor = Cursors.ScrollWE;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        // Drag right (positive delta) reveals older bars → increase the back-offset.
        double dx = e.GetPosition(this).X - _dragStartX;
        int barsDelta = (int)Math.Round(dx / Math.Max(1f, _lastBarSlotWidth));
        int visible = Math.Min(_maxVisibleBars, _bars.Count);
        int maxPan = Math.Max(0, _bars.Count - visible);
        _panBars = Math.Clamp(_dragStartPan + barsDelta, 0, maxPan);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width;
        float h = e.Info.Height;

        if (_bars.Count == 0 || w <= 0 || h <= 0)
        {
            canvas.Clear(_renderer.Palette.Background.ToSkColor());
            return;
        }

        // Footprint mode replaces the candle view with wide bid×ask columns.
        if (_studies is not null && _studies.IsEnabled("FP") && _overlayData.Footprint.Count > 0)
        {
            _footprint.Render(canvas, new SKRect(0, 0, w, h), _overlayData.Footprint, TickSize);
            return;
        }

        int visible = Math.Min(_maxVisibleBars, _bars.Count);
        int maxPan = Math.Max(0, _bars.Count - visible);
        if (_panBars > maxPan)
        {
            _panBars = maxPan; // history shrank (eviction) — keep the offset in range
        }

        int first = _bars.Count - visible - _panBars;

        long min = long.MaxValue, max = long.MinValue;
        for (int i = first; i < first + visible; i++)
        {
            min = Math.Min(min, _bars[i].LowTicks);
            max = Math.Max(max, _bars[i].HighTicks);
        }

        long pad = Math.Max(1, (max - min) / 20);
        var viewport = new ChartViewport(w, h, min - pad, max + pad, first, visible);
        _lastBarSlotWidth = viewport.BarSlotWidth;

        _renderer.Render(canvas, viewport, _bars);

        if (_studies is { Enabled.Count: > 0 } studies)
        {
            _overlays.Render(canvas, viewport, _overlayData, studies.Enabled);
        }

        // When scrolled back from live, show a small "history" hint so it is obvious
        // the view is paused rather than following the live edge.
        if (_panBars > 0)
        {
            DrawHistoryBadge(canvas, w);
        }
    }

    private static void DrawHistoryBadge(SKCanvas canvas, float w)
    {
        using var bg = new SKPaint { Color = new SKColor(0x0E, 0x0F, 0x12, 0xCC), IsAntialias = true };
        using var fg = new SKPaint { Color = new SKColor(0xC4, 0xA7, 0xFF), IsAntialias = true, TextSize = 12 };
        const string text = "◂ history — drag right edge to resume live";
        float tw = fg.MeasureText(text);
        var rect = new SKRect(w - tw - 24, 8, w - 8, 28);
        canvas.DrawRoundRect(rect, 4, 4, bg);
        canvas.DrawText(text, rect.Left + 8, rect.Bottom - 6, fg);
    }
}
