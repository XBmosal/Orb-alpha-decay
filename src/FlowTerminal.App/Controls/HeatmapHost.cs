using System.Windows;
using System.Windows.Input;
using FlowTerminal.Charting;
using FlowTerminal.Charting.Heatmap;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Single-canvas Bookmap-style liquidity heatmap. Painting is delegated to the feed
/// (which holds the tiled history under its own lock); this control owns the view
/// state and interaction: drag the plot to pan time/price, drag the right axis to
/// zoom price, drag the bottom strip to zoom time, scroll to zoom, hover for the
/// crosshair readout, and double-click to reset. Observational only.
/// </summary>
public sealed class HeatmapHost : SKElement
{
    private const float RightAxis = 64f;
    private const float BottomZone = 68f; // volume strip + time axis

    private readonly BookmapView _view = new();
    private LiveFeedService? _feed;

    private enum DragMode { None, Pan, PriceZoom, TimeZoom }

    private DragMode _drag = DragMode.None;
    private Point _start;
    private double _startPricePan, _startTimePan, _startPriceZoom, _startTimeZoom;

    public void Attach(LiveFeedService feed)
    {
        _feed = feed;
        InvalidateVisual();
    }

    private float PlotW => (float)Math.Max(1, ActualWidth - RightAxis);
    private float PlotH => (float)Math.Max(1, ActualHeight - BottomZone);

    private DragMode RegionAt(Point p) =>
        p.X >= ActualWidth - RightAxis ? DragMode.PriceZoom
        : p.Y >= ActualHeight - BottomZone ? DragMode.TimeZoom
        : DragMode.Pan;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { _view.Reset(); InvalidateVisual(); return; }

        _start = e.GetPosition(this);
        _drag = RegionAt(_start);
        _startPricePan = _view.PricePanFrac;
        _startTimePan = _view.TimePanFrac;
        _startPriceZoom = _view.PriceZoom;
        _startTimeZoom = _view.TimeZoom;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);

        if (_drag == DragMode.None)
        {
            _view.Cursor = new SKPoint((float)p.X, (float)p.Y);
            InvalidateVisual();
            return;
        }

        double dx = p.X - _start.X, dy = p.Y - _start.Y;
        switch (_drag)
        {
            case DragMode.Pan:
                _view.PricePanFrac = Math.Clamp(_startPricePan + dy / PlotH * 2.0, -4, 4);
                _view.TimePanFrac = Math.Max(0, _startTimePan + dx / PlotW);
                break;
            case DragMode.PriceZoom:
                _view.PriceZoom = Math.Clamp(_startPriceZoom * Math.Exp((_start.Y - p.Y) * 0.006), 0.25, 20);
                break;
            case DragMode.TimeZoom:
                _view.TimeZoom = Math.Clamp(_startTimeZoom * Math.Exp(dx * 0.006), 0.25, 20);
                break;
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

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _view.Cursor = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
        if (e.GetPosition(this).X >= ActualWidth - RightAxis)
            _view.PriceZoom = Math.Clamp(_view.PriceZoom * factor, 0.25, 20);
        else
            _view.TimeZoom = Math.Clamp(_view.TimeZoom * factor, 0.25, 20);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        if (_feed is null)
        {
            canvas.Clear();
            return;
        }

        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);
        if (!RenderSafety.Guard(canvas, bounds, () => _feed.RenderBookmap(canvas, bounds, _view), label: "Heatmap view paused"))
        {
            RenderGuard.LogThrottled("heatmap");
        }
    }
}
