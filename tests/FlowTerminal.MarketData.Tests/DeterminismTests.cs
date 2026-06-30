using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Synthetic;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

public class DeterminismTests
{
    private static readonly Contract Nq = new(RootSymbol.NQ, QuarterlyMonth.December, 2025);
    private static readonly DateTime Start = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Rng_Same_Seed_Produces_Same_Sequence()
    {
        var a = new DeterministicRng(12345);
        var b = new DeterministicRng(12345);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void Rng_Different_Seed_Diverges()
    {
        var a = new DeterministicRng(1);
        var b = new DeterministicRng(2);
        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Generator_Is_Deterministic_For_Same_Seed()
    {
        var opts = new SyntheticOptions { Seed = 777 };
        var g1 = new SyntheticSessionGenerator(1, Nq, Start, opts);
        var g2 = new SyntheticSessionGenerator(1, Nq, Start, opts);

        for (int i = 0; i < 5000; i++)
        {
            var e1 = g1.Next();
            var e2 = g2.Next();
            Assert.Equal(e1.Type, e2.Type);
            Assert.Equal(e1.PriceTicks, e2.PriceTicks);
            Assert.Equal(e1.Quantity, e2.Quantity);
            Assert.Equal(e1.ExchangeSequence, e2.ExchangeSequence);
            Assert.Equal(e1.ExchangeTimestampUtc, e2.ExchangeTimestampUtc);
            Assert.Equal(e1.Aggressor, e2.Aggressor);
        }
    }

    [Fact]
    public void Generator_Sequences_Are_Monotonic_By_Default()
    {
        var g = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 5 });
        long last = 0;
        foreach (var e in g.Generate(2000))
        {
            Assert.True(e.ExchangeSequence > last);
            last = e.ExchangeSequence;
        }
    }

    [Fact]
    public void Generator_Timestamps_Never_Go_Backward()
    {
        var g = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 9 });
        DateTime last = DateTime.MinValue;
        foreach (var e in g.Generate(2000))
        {
            Assert.True(e.ExchangeTimestampUtc >= last);
            last = e.ExchangeTimestampUtc;
        }
    }

    [Fact]
    public void Generator_Flags_Every_Event_As_Synthetic()
    {
        var g = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 3 });
        foreach (var e in g.Generate(500))
        {
            Assert.True(e.HasFlag(MarketEventFlags.Synthetic));
            Assert.Equal(SourceProvider.Unknown, e.Source); // generator leaves source to the provider
        }
    }

    [Fact]
    public void Generator_Injects_Gap_When_Requested()
    {
        var g = new SyntheticSessionGenerator(1, Nq, Start, new SyntheticOptions { Seed = 1, InjectGapAfter = 10 });
        var events = g.Generate(20).ToArray();
        bool foundGap = false;
        for (int i = 1; i < events.Length; i++)
        {
            if (events[i].ExchangeSequence - events[i - 1].ExchangeSequence > 1)
            {
                foundGap = true;
            }
        }

        Assert.True(foundGap);
    }
}
