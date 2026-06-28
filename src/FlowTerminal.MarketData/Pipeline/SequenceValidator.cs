using FlowTerminal.Domain.Events;

namespace FlowTerminal.MarketData.Pipeline;

public enum SequenceResult
{
    /// <summary>Event is the expected next sequence (or the first event of a stream).</summary>
    Ok,

    /// <summary>Event carries no sequence number; ordering cannot be validated by sequence.</summary>
    NoSequence,

    /// <summary>Sequence is older than or equal to the last seen — a duplicate or out-of-order replay.</summary>
    Duplicate,

    /// <summary>A gap was detected: one or more events are missing. The book must be invalidated.</summary>
    Gap,
}

/// <summary>
/// Per-instrument monotonic sequence checker. When the feed supplies exchange
/// sequence numbers, this detects gaps (missing events) and duplicates. A gap is
/// a hard integrity failure: the caller must invalidate affected calculations and
/// resynchronize from a snapshot rather than continue on a corrupt book.
///
/// Single-writer by contract: one instance per instrument, driven by that
/// instrument's single processing thread.
/// </summary>
public sealed class SequenceValidator
{
    private long _lastSequence = -1;
    private bool _hasSequence;

    public long LastSequence => _lastSequence;

    public bool HasSeenSequence => _hasSequence;

    public SequenceResult Check(in MarketEvent marketEvent)
    {
        if (!marketEvent.HasFlag(MarketEventFlags.HasExchangeSequence))
        {
            return SequenceResult.NoSequence;
        }

        long seq = marketEvent.ExchangeSequence;

        if (!_hasSequence)
        {
            _hasSequence = true;
            _lastSequence = seq;
            return SequenceResult.Ok;
        }

        if (seq == _lastSequence + 1)
        {
            _lastSequence = seq;
            return SequenceResult.Ok;
        }

        if (seq <= _lastSequence)
        {
            return SequenceResult.Duplicate;
        }

        // seq > last + 1: a gap. Advance to the new sequence so we can resync forward.
        _lastSequence = seq;
        return SequenceResult.Gap;
    }

    /// <summary>Resets validator state, e.g. after a book clear, reconnect, or snapshot start.</summary>
    public void Reset()
    {
        _lastSequence = -1;
        _hasSequence = false;
    }
}
