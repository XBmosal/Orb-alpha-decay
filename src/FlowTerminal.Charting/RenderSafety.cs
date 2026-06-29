using SkiaSharp;

namespace FlowTerminal.Charting;

/// <summary>
/// Render-fault isolation for the Skia panels. A drawing operation that throws must
/// never propagate into WPF's render loop (where it would become a fatal, whole-app
/// exception): instead the panel paints a calm, themed "view paused" state and keeps
/// the rest of the terminal alive. This is the visual boundary for the spec's rule
/// that one failed component does not destroy the application.
///
/// Pure drawing only — it does no logging (the host logs, throttled, so this stays a
/// dependency-free Charting helper).
/// </summary>
public static class RenderSafety
{
    /// <summary>
    /// Runs <paramref name="draw"/> and, on any exception, paints a themed error state
    /// over <paramref name="bounds"/>. Returns true if the draw completed, false if it
    /// faulted (so the caller can throttle logging). Never throws.
    /// </summary>
    public static bool Guard(SKCanvas canvas, SKRect bounds, Action draw, ChartPalette? palette = null, string label = "View paused")
    {
        int saveCount = canvas.SaveCount;
        try
        {
            draw();
            return true;
        }
        catch
        {
            try
            {
                // A fault mid-draw can leave the save/clip stack unbalanced; restore it
                // before painting the error card so the message is not clipped away.
                canvas.RestoreToCount(saveCount);
                DrawErrorState(canvas, bounds, palette ?? ChartPalette.Default, label);
            }
            catch { /* never let the fallback throw */ }

            return false;
        }
    }

    /// <summary>Paints a restrained, themed error card: panel background + a centred warning line.</summary>
    public static void DrawErrorState(SKCanvas canvas, SKRect bounds, ChartPalette palette, string label)
    {
        using (var bg = new SKPaint { Color = palette.PanelBackground.ToSkColor() })
        {
            canvas.DrawRect(bounds, bg);
        }

        string title = "⚠  " + label;
        const string hint = "A render error was isolated — other panels are unaffected.";

        using var titlePaint = new SKPaint
        {
            Color = palette.Warning.ToSkColor(),
            IsAntialias = true,
            Typeface = SKTypeface.Default,
            TextSize = 13f,
            TextAlign = SKTextAlign.Center,
        };
        using var hintPaint = new SKPaint
        {
            Color = palette.MutedText.ToSkColor(),
            IsAntialias = true,
            Typeface = SKTypeface.Default,
            TextSize = 11f,
            TextAlign = SKTextAlign.Center,
        };

        float cx = bounds.MidX;
        float cy = bounds.MidY;
        canvas.DrawText(title, cx, cy - 4f, titlePaint);
        canvas.DrawText(hint, cx, cy + 14f, hintPaint);
    }
}
