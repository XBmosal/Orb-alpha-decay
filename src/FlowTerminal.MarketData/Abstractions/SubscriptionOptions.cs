namespace FlowTerminal.MarketData.Abstractions;

/// <summary>
/// What data streams to request for a contract. A provider only honours the
/// streams its capabilities support; unsupported requests are reported back via
/// diagnostics rather than silently faked.
/// </summary>
public sealed record SubscriptionOptions
{
    public bool Trades { get; init; } = true;

    public bool TopOfBook { get; init; } = true;

    public bool MarketByPriceDepth { get; init; } = true;

    public bool MarketByOrderDepth { get; init; }

    /// <summary>Requested visible depth levels for market-by-price aggregation.</summary>
    public int DepthLevels { get; init; } = 20;

    public static SubscriptionOptions Default { get; } = new();
}
