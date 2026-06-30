using FlowTerminal.Charting;
using FlowTerminal.Charting.Dom;
using FlowTerminal.Domain.Instruments;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace FlowTerminal.App.Controls;

/// <summary>
/// Read-only depth-of-market ladder rendered on a single Skia canvas (no per-cell WPF
/// controls). The view-model snapshot (rows + resolved column layout) is supplied each
/// frame by the feed; the control only paints. Strictly observational — there are no
/// order-entry surfaces. Render faults are isolated so a bad frame never crashes the app.
/// </summary>
public sealed class DomHost : SKElement
{
    private readonly DomLadderRenderer _renderer = new();
    private IReadOnlyList<DomRow> _rows = Array.Empty<DomRow>();
    private IReadOnlyList<DomColumnType> _columns = DomPresetRegistry.Default.Columns;
    private InstrumentSpec? _spec;
    private decimal _tick = 0.25m;

    public void Update(IReadOnlyList<DomRow> rows, IReadOnlyList<DomColumnType> columns, InstrumentSpec? spec, decimal tickSize)
    {
        _rows = rows;
        _columns = columns;
        _spec = spec;
        _tick = tickSize;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);
        if (!RenderSafety.Guard(canvas, bounds, () => _renderer.Render(canvas, bounds, _rows, _columns, _tick, _spec), label: "DOM view paused"))
        {
            RenderGuard.LogThrottled("dom");
        }
    }
}
