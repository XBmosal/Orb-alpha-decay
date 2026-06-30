using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Pipeline;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

public class SequenceValidatorTests
{
    private static MarketEvent WithSeq(long seq, bool hasSeq = true) => MarketEvent.Quote(
        1, RootSymbol.NQ, "NQZ5", "CME Globex", MarketEventType.BidUpdate,
        DateTime.UnixEpoch, DateTime.UnixEpoch, Side.Bid, 100, 1,
        exchangeSequence: seq,
        flags: hasSeq ? MarketEventFlags.HasExchangeSequence : MarketEventFlags.None);

    [Fact]
    public void First_Event_Is_Ok()
    {
        var v = new SequenceValidator();
        Assert.Equal(SequenceResult.Ok, v.Check(WithSeq(100)));
    }

    [Fact]
    public void Consecutive_Sequences_Are_Ok()
    {
        var v = new SequenceValidator();
        v.Check(WithSeq(100));
        Assert.Equal(SequenceResult.Ok, v.Check(WithSeq(101)));
        Assert.Equal(SequenceResult.Ok, v.Check(WithSeq(102)));
    }

    [Fact]
    public void Repeated_Or_Older_Sequence_Is_Duplicate()
    {
        var v = new SequenceValidator();
        v.Check(WithSeq(100));
        v.Check(WithSeq(101));
        Assert.Equal(SequenceResult.Duplicate, v.Check(WithSeq(101)));
        Assert.Equal(SequenceResult.Duplicate, v.Check(WithSeq(50)));
    }

    [Fact]
    public void Missing_Sequence_Is_Gap()
    {
        var v = new SequenceValidator();
        v.Check(WithSeq(100));
        Assert.Equal(SequenceResult.Gap, v.Check(WithSeq(105)));
        // After a gap, the validator advances and resumes from the new sequence.
        Assert.Equal(SequenceResult.Ok, v.Check(WithSeq(106)));
    }

    [Fact]
    public void Events_Without_Sequence_Report_NoSequence()
    {
        var v = new SequenceValidator();
        Assert.Equal(SequenceResult.NoSequence, v.Check(WithSeq(0, hasSeq: false)));
    }

    [Fact]
    public void Reset_Clears_State()
    {
        var v = new SequenceValidator();
        v.Check(WithSeq(100));
        v.Reset();
        Assert.False(v.HasSeenSequence);
        Assert.Equal(SequenceResult.Ok, v.Check(WithSeq(5)));
    }
}
