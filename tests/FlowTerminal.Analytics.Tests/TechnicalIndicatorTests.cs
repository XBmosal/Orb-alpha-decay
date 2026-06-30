using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Indicators;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

/// <summary>
/// Reference-vector and invariant tests for the standard technical-indicator library.
/// Where a closed-form value exists it is asserted directly; otherwise documented
/// invariants (bounds, warm-up, NaN-safety, monotonic-input behaviour, incremental ==
/// batch) stand in for a reference vector.
/// </summary>
public class TechnicalIndicatorTests
{
    private const double Tol = 1e-9;
    private static void Close(double expected, double actual, double tol = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");

    private static Bar Bar(long o, long h, long l, long c, long vol = 100) =>
        new(BarKind.Time, default, default, o, h, l, c, vol, vol / 2, vol / 2, 10);

    // ── Moving averages ─────────────────────────────────────────────────────

    [Fact]
    public void Sma_Matches_Reference_Vector()
    {
        var ma = new MovingAverage(3, MovingAverageType.Sma);
        double[] input = { 1, 2, 3, 4, 5 };
        double[] expect = { double.NaN, double.NaN, 2, 3, 4 };
        for (int i = 0; i < input.Length; i++)
        {
            ma.OnValue(input[i]);
            if (double.IsNaN(expect[i])) Assert.False(ma.IsReady);
            else Close(expect[i], ma.Value);
        }
    }

    [Fact]
    public void Ema_Matches_Reference_Vector()
    {
        var ma = new MovingAverage(3, MovingAverageType.Ema); // alpha = 0.5, SMA seed
        double[] input = { 1, 2, 3, 4, 5 };
        double[] expect = { double.NaN, double.NaN, 2, 3, 4 };
        for (int i = 0; i < input.Length; i++)
        {
            ma.OnValue(input[i]);
            if (double.IsNaN(expect[i])) Assert.False(ma.IsReady);
            else Close(expect[i], ma.Value);
        }
    }

    [Fact]
    public void Wma_Matches_Reference_Vector()
    {
        var ma = new MovingAverage(3, MovingAverageType.Wma);
        ma.OnValue(1); ma.OnValue(2); ma.OnValue(3);
        Close(14.0 / 6.0, ma.Value); // (1*1+2*2+3*3)/6
        ma.OnValue(4);
        Close(20.0 / 6.0, ma.Value); // (2*1+3*2+4*3)/6
    }

    [Fact]
    public void Rma_Matches_Reference_Vector()
    {
        var ma = new MovingAverage(3, MovingAverageType.Rma); // alpha = 1/3, SMA seed
        foreach (var v in new double[] { 1, 2, 3 }) ma.OnValue(v);
        Close(2.0, ma.Value);          // seed = mean(1,2,3)
        ma.OnValue(4);
        Close(2.0 + (4 - 2.0) / 3.0, ma.Value); // 2.6667
    }

    [Fact]
    public void Hma_Returns_Constant_On_Constant_Input()
    {
        var ma = new MovingAverage(4, MovingAverageType.Hma);
        for (int i = 0; i < 10; i++) ma.OnValue(7.0);
        Assert.True(ma.IsReady);
        Close(7.0, ma.Value, 1e-9);
    }

    [Fact]
    public void MovingAverage_Incremental_Equals_Independent_Batch()
    {
        var rng = new Random(42);
        var data = new double[200];
        for (int i = 0; i < data.Length; i++) data[i] = 100 + rng.NextDouble() * 50;

        var sma = new MovingAverage(14, MovingAverageType.Sma);
        for (int i = 0; i < data.Length; i++)
        {
            sma.OnValue(data[i]);
            if (i >= 13)
            {
                double batch = 0;
                for (int j = i - 13; j <= i; j++) batch += data[j];
                batch /= 14;
                Close(batch, sma.Value, 1e-9);
            }
        }
    }

    // ── Momentum / oscillators ──────────────────────────────────────────────

    [Fact]
    public void Rsi_Is_100_On_Monotonic_Rise_And_0_On_Monotonic_Fall()
    {
        var up = new Rsi(14);
        for (int i = 1; i <= 40; i++) up.OnValue(i);
        Assert.True(up.IsReady);
        Close(100.0, up.Value, 1e-6);

        var down = new Rsi(14);
        for (int i = 40; i >= 1; i--) down.OnValue(i);
        Close(0.0, down.Value, 1e-6);
    }

    [Fact]
    public void Rsi_Stays_Bounded_And_Finite()
    {
        var rsi = new Rsi(14);
        var rng = new Random(7);
        double p = 100;
        for (int i = 0; i < 500; i++)
        {
            p += rng.NextDouble() * 4 - 2;
            rsi.OnValue(p);
            if (rsi.IsReady)
            {
                Assert.False(double.IsNaN(rsi.Value) || double.IsInfinity(rsi.Value));
                Assert.InRange(rsi.Value, 0.0, 100.0);
            }
        }
    }

    [Fact]
    public void RateOfChange_And_Momentum_Match_Reference()
    {
        var roc = new RateOfChange(2);
        var mom = new Momentum(2);
        foreach (var v in new double[] { 10, 11, 12 }) { roc.OnValue(v); mom.OnValue(v); }
        Close(20.0, roc.Value);  // 100*(12-10)/10
        Close(2.0, mom.Value);   // 12-10
    }

    [Fact]
    public void Stochastic_Is_100_At_Top_And_0_At_Bottom_Of_Range()
    {
        var top = new Stochastic(5, 1, 1);
        for (long i = 0; i < 10; i++) top.OnBar(Bar(i, i + 10, i, i + 10)); // close == high
        Assert.True(top.IsReady);
        Close(100.0, top.K, 1e-6);

        var bottom = new Stochastic(5, 1, 1);
        for (long i = 0; i < 10; i++)
        {
            long lo = 100 - i * 5; // strictly falling → current low is the window minimum
            bottom.OnBar(Bar(lo + 10, lo + 10, lo, lo)); // close == low == lowest low
        }

        Close(0.0, bottom.K, 1e-6);
    }

    [Fact]
    public void Cci_Is_Zero_On_Flat_Typical_Price()
    {
        var cci = new CommodityChannelIndex(10);
        for (int i = 0; i < 20; i++) cci.OnBar(Bar(100, 100, 100, 100));
        Assert.True(cci.IsReady);
        Close(0.0, cci.Value, Tol);
    }

    // ── Volatility ──────────────────────────────────────────────────────────

    [Fact]
    public void Atr_Converges_To_Constant_True_Range()
    {
        var atr = new AverageTrueRange(14);
        for (int i = 0; i < 60; i++) atr.OnBar(Bar(9, 10, 8, 9)); // range 2, close mid → TR stays 2
        Assert.True(atr.IsReady);
        Close(2.0, atr.Value, 1e-6);
    }

    [Fact]
    public void Adx_Trends_To_High_With_Plus_Di_Dominant_On_Strong_Uptrend()
    {
        var adx = new AverageDirectionalIndex(14);
        for (long i = 0; i < 80; i++)
        {
            long lo = 100 + i * 5;
            adx.OnBar(Bar(lo, lo + 4, lo, lo + 4)); // each bar steps up, no overlap → only +DM
        }

        Assert.True(adx.IsReady);
        Assert.InRange(adx.Value, 0.0, 100.0);
        Assert.True(adx.Value > 90, $"ADX should be very high on a clean trend, got {adx.Value}");
        Assert.True(adx.PlusDi > adx.MinusDi);
        Close(0.0, adx.MinusDi, 1e-6);
    }

    // ── Trend / channels ────────────────────────────────────────────────────

    [Fact]
    public void Macd_Is_Zero_On_Constant_Input()
    {
        var macd = new Macd(12, 26, 9);
        for (int i = 0; i < 100; i++) macd.OnValue(50);
        Assert.True(macd.IsReady);
        Close(0.0, macd.MacdLine, 1e-6);
        Close(0.0, macd.Signal, 1e-6);
        Close(0.0, macd.Histogram, 1e-6);
    }

    [Fact]
    public void BollingerBands_Match_Reference_And_Collapse_When_Flat()
    {
        var bb = new BollingerBands(3, 2.0);
        bb.OnValue(2); bb.OnValue(4); bb.OnValue(6);
        Close(4.0, bb.Middle);
        double sd = Math.Sqrt((4 + 0 + 4) / 3.0); // population stddev of {2,4,6}
        Close(4.0 + 2 * sd, bb.Upper);
        Close(4.0 - 2 * sd, bb.Lower);

        var flat = new BollingerBands(5, 2.0);
        for (int i = 0; i < 10; i++) flat.OnValue(20);
        Close(20.0, flat.Upper, Tol);
        Close(20.0, flat.Lower, Tol);
        Close(0.0, flat.Bandwidth, Tol);
    }

    [Fact]
    public void Donchian_Tracks_Extremes()
    {
        var dc = new DonchianChannel(3);
        dc.OnBar(Bar(10, 12, 8, 11));
        dc.OnBar(Bar(11, 15, 9, 14));
        dc.OnBar(Bar(14, 13, 7, 10));
        Assert.True(dc.IsReady);
        Close(15.0, dc.Upper);
        Close(7.0, dc.Lower);
        Close(11.0, dc.Middle);
    }

    [Fact]
    public void Keltner_Brackets_The_Midline()
    {
        var kc = new KeltnerChannel(20, 10, 2.0);
        var rng = new Random(3);
        long c = 1000;
        for (int i = 0; i < 80; i++)
        {
            c += rng.Next(-3, 4);
            kc.OnBar(Bar(c, c + 5, c - 5, c));
        }

        Assert.True(kc.IsReady);
        Assert.True(kc.Upper > kc.Middle && kc.Middle > kc.Lower);
    }

    // ── Cross-cutting guarantees ────────────────────────────────────────────

    [Fact]
    public void Indicators_Ignore_Nan_And_Infinity_Inputs()
    {
        var ma = new MovingAverage(3, MovingAverageType.Sma);
        ma.OnValue(1); ma.OnValue(double.NaN); ma.OnValue(2);
        ma.OnValue(double.PositiveInfinity); ma.OnValue(3);
        Assert.True(ma.IsReady);
        Close(2.0, ma.Value); // (1+2+3)/3 — invalid inputs were ignored
    }

    [Fact]
    public void Reset_Clears_State_And_Reproduces_Sequence()
    {
        var ma = new MovingAverage(5, MovingAverageType.Ema);
        double[] data = { 3, 1, 4, 1, 5, 9, 2, 6 };
        foreach (var v in data) ma.OnValue(v);
        double first = ma.Value;

        ma.Reset();
        Assert.False(ma.IsReady);
        Assert.True(double.IsNaN(ma.Value));

        foreach (var v in data) ma.OnValue(v);
        Close(first, ma.Value, Tol);
    }

    [Fact]
    public void Catalog_Builds_Every_Indicator_With_Defaults()
    {
        Assert.NotEmpty(IndicatorCatalog.All);
        foreach (var e in IndicatorCatalog.All)
        {
            var ind = e.Create();
            Assert.False(ind.IsReady); // nothing is ready before any data
            Assert.False(string.IsNullOrWhiteSpace(ind.Descriptor.Id));
            Assert.Equal(e.Descriptor.Id, ind.Descriptor.Id);
        }
    }
}
