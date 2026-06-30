using System.Windows.Input;
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
///
/// Two read-only study aids: <see cref="Frozen"/> holds the ladder so a level can be
/// studied while the market moves, and hovering a row shows an inspector with that
/// price's full breakdown. Neither acts on the market in any way.
/// </summary>
public sealed class DomHost : SKElement
{
    // Must mirror the renderer's row geometry so hover maps to the right row.
    private const float HeaderH = 20f;
    private const float Pad = 8f;

    private readonly DomLadderRenderer _renderer = new();
    private IReadOnlyList<DomRow> _rows = Array.Empty<DomRow>();
    private IReadOnlyList<DomColumnType> _columns = DomPresetRegistry.Default.Columns;
    private IReadOnlyList<double>? _widths;
    private InstrumentSpec? _spec;
    private decimal _tick = 0.25m;
    private bool _frozen;
    private long? _focusPrice;

    /// <summary>Holds the ladder for study; live readouts elsewhere keep updating.</summary>
    public bool Frozen
    {
        get => _frozen;
        set { if (_frozen == value) return; _frozen = value; InvalidateVisual(); }
    }

    public void Update(IReadOnlyList<DomRow> rows, IReadOnlyList<DomColumnType> columns,
        IReadOnlyList<double>? widths, InstrumentSpec? spec, decimal tickSize)
    {
        // While frozen the ladder rows are held; layout (columns/widths) still applies so
        // the column editor stays responsive.
        if (!_frozen)
        {
            _rows = rows;
            _spec = spec;
            _tick = tickSize;
        }
        _columns = columns;
        _widths = widths;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_rows.Count == 0 || ActualHeight <= 0 || CanvasSize.Height <= 0)
        {
            ClearFocus();
            return;
        }

        // Map the cursor (WPF units) into canvas pixels, then into a row index using the
        // same geometry the renderer lays out.
        double py = e.GetPosition(this).Y * (CanvasSize.Height / ActualHeight);
        float rowH = Math.Max(12f, ((float)CanvasSize.Height - HeaderH - Pad) / _rows.Count);
        int idx = (int)((py - HeaderH) / rowH);
        long? focus = idx >= 0 && idx < _rows.Count ? _rows[idx].PriceTicks : null;
        if (focus != _focusPrice)
        {
            _focusPrice = focus;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearFocus();
    }

    private void ClearFocus()
    {
        if (_focusPrice is null) return;
        _focusPrice = null;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);
        if (!RenderSafety.Guard(canvas, bounds,
                () => _renderer.Render(canvas, bounds, _rows, _columns, _tick, _spec, _widths, _frozen, _focusPrice),
                label: "DOM view paused"))
        {
            RenderGuard.LogThrottled("dom");
        }
    }
}
