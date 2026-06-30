using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.PriceAction;
using FlowTerminal.Analytics.Profiles;
using FlowTerminal.Analytics.Volume;
using FlowTerminal.Analytics.Vwap;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

public class FairValueGapTests
{
    private static Bar Bar(long high, long low) =>
        new(BarKind.Time, DateTime.UnixEpoch, DateTime.UnixEpoch, low, high, low, high, 10, 5, 5, 1);

    [Fact]
    public void Detects_Bullish_Gap()
    {
        var d = new FairValueGapDetector();
        Assert.Null(d.OnBar(Bar(100, 95)));   // bar i-2: high 100
        Assert.Null(d.OnBar(Bar(108, 101)));  // middle bar drives up
        var fvg = d.OnBar(Bar(112, 104));     // bar i: low 104 > 100 → gap (100,104)
        Assert.NotNull(fvg);
        Assert.Equal(GapDirection.Bullish, fvg!.Value.Direction);
        Assert.Equal(104, fvg.Value.TopTicks);
        Assert.Equal(100, fvg.Value.BottomTicks);
        Assert.Equal(4, fvg.Value.SizeTicks);
    }

    [Fact]
    public void No_Gap_When_Bars_Overlap()
    {
        var d = new FairValueGapDetector();
        d.OnBar(Bar(100, 95));
        d.OnBar(Bar(101, 96));
        Assert.Null(d.OnBar(Bar(102, 97))); // low 97 < prev-prev high 100 → no gap
    }
}

public class GapAndRangeTests
{
    [Fact]
    public void Gap_Up_And_Down_Classified()
    {
        Assert.Equal((GapDirection.Bullish, 12L), GapDetector.Classify(prevCloseTicks: 100, openTicks: 112));
        Assert.Equal((GapDirection.Bearish, 8L), GapDetector.Classify(100, 92));
        Assert.Equal((GapDirection.None, 0L), GapDetector.Classify(100, 100));
    }

    [Fact]
    public void Adr_Averages_Ranges_And_Projects_Targets()
    {
        var adr = new AverageDailyRange(period: 3);
        adr.AddDay(120, 100); // 20
        adr.AddDay(140, 100); // 40
        adr.AddDay(130, 100); // 30  → avg 30
        Assert.Equal(30.0, adr.AverageTicks, 6);
        var (up, down) = adr.TargetsFromOpen(1000);
        Assert.Equal(1015, up);   // +15 (half of 30)
        Assert.Equal(985, down);
    }

    [Fact]
    public void Orb_Flags_First_Breakout_Above_Range()
    {
        var end = new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);
        var orb = new OpeningRangeBreakout(end);
        orb.OnTrade(100, end.AddMinutes(-5));
        orb.OnTrade(110, end.AddMinutes(-3)); // range high 110
        orb.OnTrade(95, end.AddMinutes(-1));  // range low 95
        Assert.Equal(GapDirection.None, orb.OnTrade(108, end.AddMinutes(1)));   // inside
        Assert.Equal(GapDirection.Bullish, orb.OnTrade(111, end.AddMinutes(2))); // breaks high
        Assert.Equal(GapDirection.Bullish, orb.BreakoutDirection);
    }
}

public class ChaikinAdTests
{
    [Fact]
    public void Accumulation_Pushes_Line_Up()
    {
        var ad = new ChaikinAccumulationDistribution();
        // Close at the high → MFM = +1 → ADL += volume
        var bar = new Bar(BarKind.Time, DateTime.UnixEpoch, DateTime.UnixEpoch, 100, 110, 90, 110, 100, 60, 40, 5);
        Assert.Equal(100.0, ad.OnBar(bar), 6);
    }

    [Fact]
    public void Flat_Bar_Does_Not_Divide_By_Zero()
    {
        var ad = new ChaikinAccumulationDistribution();
        var bar = new Bar(BarKind.Time, DateTime.UnixEpoch, DateTime.UnixEpoch, 100, 100, 100, 100, 100, 50, 50, 5);
        Assert.Equal(0.0, ad.OnBar(bar));
    }
}

public class TpoProfileTests
{
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Records_Brackets_Per_Price_And_Finds_Poc()
    {
        var tpo = new TpoProfile(Start, TimeSpan.FromMinutes(30));
        // Bracket A (0-30m): prices 100, 101
        tpo.AddTrade(100, Start.AddMinutes(5));
        tpo.AddTrade(101, Start.AddMinutes(20));
        // Bracket B (30-60m): price 100 again
        tpo.AddTrade(100, Start.AddMinutes(35));

        Assert.Equal(2, tpo.TpoCountAt(100)); // brackets A and B
        Assert.Equal(1, tpo.TpoCountAt(101));
        Assert.Equal("AB", tpo.LettersAt(100));
        Assert.Equal(100, tpo.PocTicks());
    }
}

public class MultiVwapTests
{
    [Fact]
    public void Daily_Resets_On_New_Trading_Date()
    {
        var v = new MultiVwap();
        v.AddTrade(100, 10, new DateOnly(2024, 6, 3));
        v.AddTrade(102, 10, new DateOnly(2024, 6, 3));
        Assert.Equal(101.0, v.Daily.VwapTicks, 6);

        v.AddTrade(200, 5, new DateOnly(2024, 6, 4)); // new day → daily resets
        Assert.Equal(200.0, v.Daily.VwapTicks, 6);
        // Monthly still accumulates across the two days.
        Assert.True(v.Monthly.VwapTicks is > 100 and < 200);
    }

    [Fact]
    public void Anchored_Starts_Empty_Until_Anchored()
    {
        var v = new MultiVwap();
        v.AddTrade(100, 10, new DateOnly(2024, 6, 3));
        Assert.False(v.HasAnchor);
        v.Anchor();
        v.AddTrade(200, 10, new DateOnly(2024, 6, 3));
        Assert.Equal(200.0, v.Anchored.VwapTicks, 6); // only post-anchor trades
    }
}
