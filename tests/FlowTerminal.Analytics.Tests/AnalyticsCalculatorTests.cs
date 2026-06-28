using FlowTerminal.Analytics.Delta;
using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Analytics.Vwap;
using FlowTerminal.Domain.Events;
using Xunit;
using static FlowTerminal.Analytics.Tests.TestTrades;

namespace FlowTerminal.Analytics.Tests;

public class CvdTests
{
    [Fact]
    public void Cvd_Accumulates_Signed_Delta()
    {
        var cvd = new CvdCalculator();
        Assert.Equal(5, cvd.Add(At(0, 100, 5, AggressorSide.Buy)).CumulativeDelta);
        Assert.Equal(-3, cvd.Add(At(1, 100, 8, AggressorSide.Sell)).CumulativeDelta);
        Assert.Equal(2, cvd.Add(At(2, 100, 5, AggressorSide.Buy)).CumulativeDelta);
        Assert.Equal(5, cvd.MaxPositive);
        Assert.Equal(-3, cvd.MaxNegative);
    }

    [Fact]
    public void Unknown_Aggressor_Does_Not_Move_Cvd()
    {
        var cvd = new CvdCalculator();
        cvd.Add(At(0, 100, 5, AggressorSide.Buy));
        var p = cvd.Add(At(1, 100, 9, AggressorSide.Unknown));
        Assert.Equal(5, p.CumulativeDelta);
    }
}

public class VolumeProfileTests
{
    private static VolumeProfile Fixture()
    {
        var p = new VolumeProfile();
        p.AddAt(100, 10, AggressorSide.Buy);   // total 10
        p.AddAt(101, 12, AggressorSide.Buy);
        p.AddAt(101, 5, AggressorSide.Sell);   // total 17
        p.AddAt(102, 8, AggressorSide.Sell);   // total 8
        return p;
    }

    [Fact]
    public void Poc_Is_Highest_Volume_Price()
    {
        Assert.Equal(101, Fixture().PocTicks());
    }

    [Fact]
    public void Value_Area_Expands_From_Poc()
    {
        // total = 35, target = ceil(35*0.7) = 25. POC 101 (17), then below 100 (10) → 27 ≥ 25.
        var va = Fixture().ComputeValueArea(0.70);
        Assert.Equal(101, va.PocTicks);
        Assert.Equal(100, va.ValTicks);
        Assert.Equal(101, va.VahTicks);
    }

    [Fact]
    public void Delta_At_Price_Is_Buy_Minus_Sell()
    {
        Assert.Equal(7, Fixture().DeltaAt(101)); // 12 - 5
    }

    [Fact]
    public void Empty_Profile_Is_Safe()
    {
        var va = new VolumeProfile().ComputeValueArea();
        Assert.Equal(long.MinValue, va.PocTicks);
    }
}

public class FootprintTests
{
    private static Footprint Fixture()
    {
        var f = new Footprint();
        f.AddAt(100, 9, AggressorSide.Buy);   // ask@100 = 9
        f.AddAt(99, 2, AggressorSide.Sell);   // bid@99  = 2
        f.AddAt(101, 10, AggressorSide.Sell); // bid@101 = 10
        f.AddAt(102, 1, AggressorSide.Buy);   // ask@102 = 1
        f.AddAt(105, 5, AggressorSide.Buy);   // ask@105 = 5, bid@104 = 0
        return f;
    }

    [Fact]
    public void Bid_Ask_Volume_Maps_By_Aggressor()
    {
        var f = Fixture();
        Assert.Equal(9, f.AskVolumeAt(100));
        Assert.Equal(2, f.BidVolumeAt(99));
        Assert.Equal(10, f.BidVolumeAt(101));
    }

    [Fact]
    public void Diagonal_Imbalances_Match_Definition_Including_Zero_Denominator()
    {
        var markers = Fixture().DiagonalImbalances(new ImbalanceSettings { BuyRatio = 3, SellRatio = 3, MinVolume = 1 });

        Assert.Contains(markers, m => m.PriceTicks == 100 && m.Side == ImbalanceSide.Buy);   // 9 vs bid@99=2
        Assert.Contains(markers, m => m.PriceTicks == 101 && m.Side == ImbalanceSide.Sell);  // 10 vs ask@102=1
        Assert.Contains(markers, m => m.PriceTicks == 105 && m.Side == ImbalanceSide.Buy);   // 5 vs bid@104=0
        Assert.Equal(3, markers.Count);
    }

    [Fact]
    public void Imbalance_Never_Produces_NaN_Or_Infinity()
    {
        var f = new Footprint();
        f.AddAt(100, 100, AggressorSide.Buy); // ask only, no opposing bid below
        var markers = f.DiagonalImbalances();
        Assert.All(markers, m => Assert.True(m.Volume > 0));
    }

    [Fact]
    public void Footprint_Delta_Is_Total_Buy_Minus_Sell()
    {
        var f = Fixture();
        // ask(buy): 9+1+5 = 15; bid(sell): 2+10 = 12; delta = 3
        Assert.Equal(15, f.BuyVolume);
        Assert.Equal(12, f.SellVolume);
        Assert.Equal(3, f.Delta);
    }
}

public class VwapTests
{
    [Fact]
    public void Vwap_And_StdDev_Match_Hand_Computation()
    {
        var vwap = new VwapCalculator();
        vwap.AddAt(100, 10);
        vwap.AddAt(102, 10);
        var v = vwap.Value();
        // vwap = (1000 + 1020)/20 = 101; variance = (100000+104040)/20 - 101^2 = 1
        Assert.Equal(101.0, v.VwapTicks, 6);
        Assert.Equal(1.0, v.StdDevTicks, 6);
        Assert.Equal(102.0, v.UpperBand(1), 6);
        Assert.Equal(100.0, v.LowerBand(1), 6);
    }

    [Fact]
    public void Empty_Vwap_Is_Safe()
    {
        var v = new VwapCalculator().Value();
        Assert.True(double.IsNaN(v.VwapTicks));
    }

    [Fact]
    public void Reset_Anchors_New_Vwap()
    {
        var vwap = new VwapCalculator();
        vwap.AddAt(100, 10);
        vwap.Reset();
        vwap.AddAt(200, 5);
        Assert.Equal(200.0, vwap.Value().VwapTicks, 6);
    }
}
