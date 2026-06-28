using FlowTerminal.Charting;
using Xunit;

namespace FlowTerminal.UiTests;

public class ViewportTests
{
    private static ChartViewport Make() => new(
        width: 1000, height: 500,
        minPriceTicks: 1000, maxPriceTicks: 1100,
        firstBarIndex: 0, visibleBarCount: 50,
        leftPadding: 0, rightAxisWidth: 0, topPadding: 0, bottomPadding: 0);

    [Fact]
    public void Highest_Price_Maps_To_Top()
    {
        var vp = Make();
        Assert.Equal(0f, vp.PriceToY(1100), 3);   // top
        Assert.Equal(500f, vp.PriceToY(1000), 3);  // bottom
        Assert.Equal(250f, vp.PriceToY(1050), 3);  // middle
    }

    [Fact]
    public void Price_To_Y_Round_Trips()
    {
        var vp = Make();
        Assert.Equal(1075, vp.YToPrice(vp.PriceToY(1075)));
    }

    [Fact]
    public void Bars_Are_Evenly_Spaced()
    {
        var vp = Make();
        Assert.Equal(20f, vp.BarSlotWidth, 3); // 1000 / 50
        Assert.Equal(10f, vp.BarCenterX(0), 3);
        Assert.Equal(30f, vp.BarCenterX(1), 3);
    }

    [Fact]
    public void Visibility_Window_Is_Respected()
    {
        var vp = new ChartViewport(1000, 500, 1000, 1100, firstBarIndex: 100, visibleBarCount: 50);
        Assert.True(vp.IsBarVisible(120));
        Assert.False(vp.IsBarVisible(99));
        Assert.False(vp.IsBarVisible(150));
    }
}
