using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Domain.Events;

/// <summary>
/// The single canonical market event. It is a <c>readonly struct</c> so the hot
/// path can pass it by value / store it in pooled arrays and ring buffers without
/// per-event heap allocations. String fields (<see cref="ContractSymbol"/>,
/// <see cref="Exchange"/>) hold <em>shared</em> references resolved once per
/// contract, so copying an event copies only a pointer, not the string.
///
/// Prices are always integer ticks. Time is always UTC.
/// </summary>
public readonly struct MarketEvent
{
    public MarketEvent(
        int instrumentId,
        RootSymbol root,
        string contractSymbol,
        string exchange,
        MarketEventType type,
        DateTime exchangeTimestampUtc,
        DateTime receiveTimestampUtc,
        long exchangeSequence = 0,
        Side side = Side.None,
        long priceTicks = 0,
        long quantity = 0,
        long orderId = 0,
        long tradeId = 0,
        AggressorSide aggressor = AggressorSide.Unknown,
        MarketEventFlags flags = MarketEventFlags.None,
        SourceProvider source = SourceProvider.Unknown)
    {
        InstrumentId = instrumentId;
        Root = root;
        ContractSymbol = contractSymbol;
        Exchange = exchange;
        Type = type;
        ExchangeTimestampUtc = exchangeTimestampUtc;
        ReceiveTimestampUtc = receiveTimestampUtc;
        ExchangeSequence = exchangeSequence;
        Side = side;
        PriceTicks = priceTicks;
        Quantity = quantity;
        OrderId = orderId;
        TradeId = tradeId;
        Aggressor = aggressor;
        Flags = flags;
        Source = source;
    }

    public int InstrumentId { get; }

    public RootSymbol Root { get; }

    public string ContractSymbol { get; }

    public string Exchange { get; }

    public MarketEventType Type { get; }

    public DateTime ExchangeTimestampUtc { get; }

    public DateTime ReceiveTimestampUtc { get; }

    public long ExchangeSequence { get; }

    public Side Side { get; }

    public long PriceTicks { get; }

    public long Quantity { get; }

    public long OrderId { get; }

    public long TradeId { get; }

    public AggressorSide Aggressor { get; }

    public MarketEventFlags Flags { get; }

    public SourceProvider Source { get; }

    public bool HasFlag(MarketEventFlags flag) => (Flags & flag) == flag;

    // ---- Factory helpers (keep call sites readable; no allocations) ----

    public static MarketEvent Trade(
        int instrumentId, RootSymbol root, string contractSymbol, string exchange,
        DateTime exchangeTsUtc, DateTime receiveTsUtc, long priceTicks, long quantity,
        AggressorSide aggressor, long exchangeSequence = 0, long tradeId = 0,
        MarketEventFlags flags = MarketEventFlags.None, SourceProvider source = SourceProvider.Unknown) =>
        new(instrumentId, root, contractSymbol, exchange, MarketEventType.Trade,
            exchangeTsUtc, receiveTsUtc, exchangeSequence, Side.None, priceTicks, quantity,
            orderId: 0, tradeId: tradeId, aggressor: aggressor, flags: flags, source: source);

    public static MarketEvent Quote(
        int instrumentId, RootSymbol root, string contractSymbol, string exchange,
        MarketEventType type, DateTime exchangeTsUtc, DateTime receiveTsUtc,
        Side side, long priceTicks, long quantity, long exchangeSequence = 0,
        MarketEventFlags flags = MarketEventFlags.None, SourceProvider source = SourceProvider.Unknown)
    {
        if (type != MarketEventType.BidUpdate && type != MarketEventType.AskUpdate &&
            type != MarketEventType.Add && type != MarketEventType.Modify && type != MarketEventType.Cancel)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Quote helper expects a book/quote event type.");
        }

        return new(instrumentId, root, contractSymbol, exchange, type,
            exchangeTsUtc, receiveTsUtc, exchangeSequence, side, priceTicks, quantity,
            flags: flags, source: source);
    }

    public static MarketEvent Control(
        int instrumentId, RootSymbol root, string contractSymbol, string exchange,
        MarketEventType type, DateTime exchangeTsUtc, DateTime receiveTsUtc,
        long exchangeSequence = 0, MarketEventFlags flags = MarketEventFlags.None,
        SourceProvider source = SourceProvider.Unknown) =>
        new(instrumentId, root, contractSymbol, exchange, type,
            exchangeTsUtc, receiveTsUtc, exchangeSequence, flags: flags, source: source);
}
