using FlowTerminal.Charting;
using SkiaSharp;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>
/// Verifies the render-fault isolation boundary: a throwing draw never escapes, paints
/// a themed error state, and leaves the canvas save-stack balanced.
/// </summary>
public class RenderSafetyTests
{
    private static readonly SKRect Bounds = new(0, 0, 400, 200);

    [Fact]
    public void Successful_Draw_Runs_And_Returns_True()
    {
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        bool ran = false;
        bool ok = RenderSafety.Guard(canvas, Bounds, () =>
        {
            ran = true;
            using var paint = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(10, 10, 50, 50, paint);
        });

        Assert.True(ok);
        Assert.True(ran);
    }

    [Fact]
    public void Throwing_Draw_Is_Isolated_And_Paints_Error_State()
    {
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);

        bool ok = RenderSafety.Guard(canvas, Bounds, () => throw new InvalidOperationException("boom"));

        Assert.False(ok); // fault reported, not thrown

        // The error card painted something (warning amber title) — canvas is not blank.
        bool painted = false;
        for (int x = 0; x < bmp.Width && !painted; x += 2)
        for (int y = 0; y < bmp.Height && !painted; y += 2)
            if (bmp.GetPixel(x, y).Alpha > 0 && bmp.GetPixel(x, y).Red + bmp.GetPixel(x, y).Green + bmp.GetPixel(x, y).Blue > 30)
                painted = true;
        Assert.True(painted, "error state should paint a visible message");
    }

    [Fact]
    public void Guard_Restores_Unbalanced_Save_Stack_On_Fault()
    {
        using var bmp = new SKBitmap(400, 200);
        using var canvas = new SKCanvas(bmp);
        int before = canvas.SaveCount;

        RenderSafety.Guard(canvas, Bounds, () =>
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, 1, 1)); // would otherwise clip the error card away
            throw new InvalidOperationException("mid-draw fault with open save");
        });

        Assert.Equal(before, canvas.SaveCount); // stack rebalanced
    }
}
