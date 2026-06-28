using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.Storage.Parquet;

/// <summary>
/// Flat, serialization-friendly projection of <see cref="MarketEvent"/> for Parquet
/// storage. Enums are stored as their byte values; times as UTC DateTime; prices as
/// integer ticks. This is the on-disk schema for the canonical event log.
/// </summary>
public sealed class MarketEventRecord
{
    public int InstrumentId { get; set; }
    public byte Root { get; set; }
    public string ContractSymbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public byte Type { get; set; }
    public DateTime ExchangeTimestampUtc { get; set; }
    public DateTime ReceiveTimestampUtc { get; set; }
    public long ExchangeSequence { get; set; }
    public byte Side { get; set; }
    public long PriceTicks { get; set; }
    public long Quantity { get; set; }
    public long OrderId { get; set; }
    public long TradeId { get; set; }
    public byte Aggressor { get; set; }
    public int Flags { get; set; }
    public byte Source { get; set; }

    public static MarketEventRecord From(in MarketEvent e) => new()
    {
        InstrumentId = e.InstrumentId,
        Root = (byte)e.Root,
        ContractSymbol = e.ContractSymbol,
        Exchange = e.Exchange,
        Type = (byte)e.Type,
        ExchangeTimestampUtc = DateTime.SpecifyKind(e.ExchangeTimestampUtc, DateTimeKind.Utc),
        ReceiveTimestampUtc = DateTime.SpecifyKind(e.ReceiveTimestampUtc, DateTimeKind.Utc),
        ExchangeSequence = e.ExchangeSequence,
        Side = (byte)e.Side,
        PriceTicks = e.PriceTicks,
        Quantity = e.Quantity,
        OrderId = e.OrderId,
        TradeId = e.TradeId,
        Aggressor = (byte)e.Aggressor,
        Flags = (int)e.Flags,
        Source = (byte)e.Source,
    };

    public MarketEvent ToEvent() => new(
        InstrumentId,
        (RootSymbol)Root,
        ContractSymbol,
        Exchange,
        (MarketEventType)Type,
        DateTime.SpecifyKind(ExchangeTimestampUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(ReceiveTimestampUtc, DateTimeKind.Utc),
        ExchangeSequence,
        (Side)Side,
        PriceTicks,
        Quantity,
        OrderId,
        TradeId,
        (AggressorSide)Aggressor,
        (MarketEventFlags)Flags,
        (SourceProvider)Source);
}
