namespace FlowTerminal.Domain.Events;

/// <summary>Resting/quote side of an event. <see cref="None"/> means not applicable.</summary>
public enum Side : byte
{
    None = 0,
    Bid = 1,
    Ask = 2,
}

/// <summary>
/// The aggressor (taker) side of a trade. <see cref="Unknown"/> is used when the
/// feed does not supply aggressor information and it has not (yet) been inferred.
/// Inferred values must be surfaced to the user as "Estimated".
/// </summary>
public enum AggressorSide : byte
{
    Unknown = 0,
    Buy = 1,
    Sell = 2,
}

/// <summary>
/// Canonical market event types. The same set drives live charts, recording,
/// replay, order-book reconstruction, footprints, CVD, profiles, heatmaps, and
/// detectors — there is exactly one event model in the system.
/// </summary>
public enum MarketEventType : byte
{
    Unknown = 0,

    // Book mutations (market-by-order where entitled).
    Add = 1,
    Modify = 2,
    Cancel = 3,
    Execute = 4,

    // Trades and top-of-book.
    Trade = 5,
    BidUpdate = 6,
    AskUpdate = 7,

    // Book lifecycle.
    SnapshotStart = 8,
    SnapshotEnd = 9,
    BookClear = 10,

    // Out-of-band / control.
    SessionStatus = 11,
    InstrumentDefinition = 12,
    ConnectionStatus = 13,
    SequenceGap = 14,
}

/// <summary>
/// Bit flags carrying optional, source-supplied or inferred qualifiers. Inferred
/// qualifiers are distinguished from exchange-supplied ones so the UI can label
/// estimated values honestly.
/// </summary>
[Flags]
public enum MarketEventFlags : ushort
{
    None = 0,

    /// <summary>Exchange supplied an aggressor side directly.</summary>
    AggressorSupplied = 1 << 0,

    /// <summary>Aggressor side was inferred by the app (label as Estimated).</summary>
    AggressorInferred = 1 << 1,

    /// <summary>Implied liquidity (e.g. from spread/strategy legs) where supplied.</summary>
    Implied = 1 << 2,

    /// <summary>Part of a snapshot rather than a live incremental update.</summary>
    Snapshot = 1 << 3,

    /// <summary>Event carries an exchange sequence number.</summary>
    HasExchangeSequence = 1 << 4,

    /// <summary>Event carries an exchange timestamp.</summary>
    HasExchangeTimestamp = 1 << 5,

    /// <summary>Synthetic event produced by mock/replay infrastructure, not a real feed.</summary>
    Synthetic = 1 << 6,
}
