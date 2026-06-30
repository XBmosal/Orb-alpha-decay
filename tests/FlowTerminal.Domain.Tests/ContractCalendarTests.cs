using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Domain.Tests;

public class ContractCalendarTests
{
    [Fact]
    public void Enumerate_Is_Ascending_By_Expiration()
    {
        var cal = new ContractCalendar();
        var list = cal.Enumerate(RootSymbol.NQ, 2024, 2025);
        Assert.Equal(8, list.Count); // 4 quarters x 2 years
        for (int i = 1; i < list.Count; i++)
        {
            Assert.True(list[i].ExpirationDateUtc >= list[i - 1].ExpirationDateUtc);
        }
    }

    [Fact]
    public void SuggestActive_Picks_Front_Month_Before_Roll()
    {
        var cal = new ContractCalendar(rollDaysBeforeExpiration: 8);
        // Well before any quarterly expiry: front month is the next quarterly contract.
        var suggested = cal.SuggestActive(RootSymbol.ES, new DateOnly(2025, 1, 6));
        Assert.Equal(QuarterlyMonth.March, suggested.Month);
        Assert.Equal(2025, suggested.Year);
    }

    [Fact]
    public void SuggestActive_Rolls_When_Within_Window()
    {
        var cal = new ContractCalendar(rollDaysBeforeExpiration: 8);
        var march = new Contract(RootSymbol.ES, QuarterlyMonth.March, 2025);
        // Two days before March expiry → roll to June.
        var nearRoll = march.ExpirationDateUtc.AddDays(-2);
        var suggested = cal.SuggestActive(RootSymbol.ES, nearRoll);
        Assert.Equal(QuarterlyMonth.June, suggested.Month);
    }

    [Fact]
    public void IsInRolloverWindow_Reflects_Threshold()
    {
        var cal = new ContractCalendar(rollDaysBeforeExpiration: 8);
        var c = new Contract(RootSymbol.NQ, QuarterlyMonth.June, 2025);
        Assert.True(cal.IsInRolloverWindow(c, c.ExpirationDateUtc.AddDays(-3)));
        Assert.False(cal.IsInRolloverWindow(c, c.ExpirationDateUtc.AddDays(-30)));
    }
}
