using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Domain.Tests;

public class ContractTests
{
    [Fact]
    public void StandardExpiration_Is_Third_Friday()
    {
        // March 2024: 1st is a Friday → third Friday is the 15th.
        Assert.Equal(new DateOnly(2024, 3, 15), Contract.StandardExpiration(QuarterlyMonth.March, 2024));
        // December 2025: 1st is Monday → first Friday 5th → third Friday 19th.
        Assert.Equal(new DateOnly(2025, 12, 19), Contract.StandardExpiration(QuarterlyMonth.December, 2025));
    }

    [Fact]
    public void Symbol_Uses_Month_Code_And_Year()
    {
        var c = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        Assert.Equal("NQZ5", c.Symbol);
        Assert.Equal("NQZ25", c.FullSymbol);
    }

    [Theory]
    [InlineData("ESH25", RootSymbol.ES, QuarterlyMonth.March, 2025)]
    [InlineData("NQZ25", RootSymbol.NQ, QuarterlyMonth.December, 2025)]
    [InlineData("ESM2024", RootSymbol.ES, QuarterlyMonth.June, 2024)]
    public void TryParse_Reads_Symbols(string symbol, RootSymbol root, QuarterlyMonth month, int year)
    {
        Assert.True(Contract.TryParse(symbol, out var c));
        Assert.NotNull(c);
        Assert.Equal(root, c!.Root);
        Assert.Equal(month, c.Month);
        Assert.Equal(year, c.Year);
    }

    [Theory]
    [InlineData("MNQZ5")]   // micro not supported as a root
    [InlineData("CLZ5")]    // crude not supported
    [InlineData("")]
    [InlineData("NQ")]
    public void TryParse_Rejects_Unsupported(string symbol)
    {
        Assert.False(Contract.TryParse(symbol, out _));
    }

    [Fact]
    public void DaysUntilExpiration_Is_Calendar_Difference()
    {
        var c = new Contract(RootSymbol.ES, QuarterlyMonth.March, 2025);
        var today = c.ExpirationDateUtc.AddDays(-10);
        Assert.Equal(10, c.DaysUntilExpiration(today));
    }
}
