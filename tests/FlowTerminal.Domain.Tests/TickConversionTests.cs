using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Domain.Tests;

public class TickConversionTests
{
    [Fact]
    public void Nq_Spec_Matches_Published_Values()
    {
        Assert.Equal(0.25m, InstrumentRegistry.NQ.TickSize);
        Assert.Equal(20m, InstrumentRegistry.NQ.PointValue);
        Assert.Equal(5.00m, InstrumentRegistry.NQ.TickValue);
        Assert.Equal("CME Globex", InstrumentRegistry.NQ.Exchange);
        Assert.Equal("USD", InstrumentRegistry.NQ.Currency);
    }

    [Fact]
    public void Es_Spec_Matches_Published_Values()
    {
        Assert.Equal(0.25m, InstrumentRegistry.ES.TickSize);
        Assert.Equal(50m, InstrumentRegistry.ES.PointValue);
        Assert.Equal(12.50m, InstrumentRegistry.ES.TickValue);
    }

    [Theory]
    [InlineData("20000.00", 80000)]
    [InlineData("20000.25", 80001)]
    [InlineData("20000.50", 80002)]
    [InlineData("19999.75", 79999)]
    public void ToTicks_Converts_Nq_Prices(string price, long expectedTicks)
    {
        long ticks = PriceConverter.ToTicks(InstrumentRegistry.NQ, decimal.Parse(price));
        Assert.Equal(expectedTicks, ticks);
    }

    [Fact]
    public void ToTicks_And_ToPrice_RoundTrip()
    {
        var spec = InstrumentRegistry.ES;
        for (decimal p = 5000m; p <= 5005m; p += 0.25m)
        {
            long ticks = PriceConverter.ToTicks(spec, p);
            decimal back = PriceConverter.ToPrice(spec, ticks);
            Assert.Equal(p, back);
        }
    }

    [Fact]
    public void IsOnTick_Detects_OffTick_Prices()
    {
        Assert.True(PriceConverter.IsOnTick(InstrumentRegistry.NQ, 20000.25m));
        Assert.False(PriceConverter.IsOnTick(InstrumentRegistry.NQ, 20000.10m));
    }

    [Fact]
    public void TickProfitValue_Uses_TickValue()
    {
        // 4 ticks on ES at 3 contracts = 4 * 12.50 * 3 = 150.00
        decimal value = PriceConverter.TickProfitValue(InstrumentRegistry.ES, 4, 3);
        Assert.Equal(150.00m, value);
    }

    [Fact]
    public void Registry_Exposes_Only_Nq_And_Es()
    {
        Assert.Equal(2, InstrumentRegistry.All.Count);
        Assert.Contains(InstrumentRegistry.All, s => s.Root == RootSymbol.NQ);
        Assert.Contains(InstrumentRegistry.All, s => s.Root == RootSymbol.ES);
    }
}
