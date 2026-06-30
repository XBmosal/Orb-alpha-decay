namespace FlowTerminal.Charting;

/// <summary>
/// The default dark-first color palette. Every value is configurable at runtime
/// via settings; these are the defaults the product ships with.
///
/// Mandatory product requirement: bullish candles are green (#22C55E) and bearish
/// candles are light purple (#C4A7FF). Red is never the default bearish color; it
/// is reserved for critical errors only.
/// </summary>
public sealed record ChartPalette
{
    // ---- Candles (mandated defaults) ----
    public RgbaColor BullishCandle { get; init; } = RgbaColor.FromHex("#22C55E"); // green
    public RgbaColor BearishCandle { get; init; } = RgbaColor.FromHex("#C4A7FF"); // light purple
    public RgbaColor BullishWick { get; init; } = RgbaColor.FromHex("#34D77A");
    public RgbaColor BearishWick { get; init; } = RgbaColor.FromHex("#D4BDFF");

    // ---- Backgrounds / structure ----
    public RgbaColor Background { get; init; } = RgbaColor.FromHex("#0E0F12");       // near-black charcoal
    public RgbaColor PanelBackground { get; init; } = RgbaColor.FromHex("#15171C");  // slightly lighter charcoal
    public RgbaColor GridLine { get; init; } = RgbaColor.FromHex("#2A2E36");         // subtle neutral gray
    public RgbaColor Text { get; init; } = RgbaColor.FromHex("#E6E8EC");
    public RgbaColor MutedText { get; init; } = RgbaColor.FromHex("#8B909A");

    // ---- Order flow ----
    // Green/purple identity: bid shares the bullish-green family, ask the bearish
    // light-purple family, so the whole UI reads in two accents. (Configurable.)
    public RgbaColor BidLiquidity { get; init; } = RgbaColor.FromHex("#34D399");     // emerald green (bid)
    public RgbaColor AskLiquidity { get; init; } = RgbaColor.FromHex("#C4A7FF");     // light purple (ask)
    public RgbaColor PositiveDelta { get; init; } = RgbaColor.FromHex("#22C55E");    // green
    public RgbaColor NegativeDelta { get; init; } = RgbaColor.FromHex("#C4A7FF");    // light purple
    public RgbaColor NeutralVolume { get; init; } = RgbaColor.FromHex("#6B7280");    // gray
    public RgbaColor AggressiveBuyBubble { get; init; } = RgbaColor.FromHex("#22C55E");
    public RgbaColor AggressiveSellBubble { get; init; } = RgbaColor.FromHex("#C4A7FF");

    // ---- States ----
    public RgbaColor SelectedObject { get; init; } = RgbaColor.FromHex("#22D3EE");   // cyan
    public RgbaColor Warning { get; init; } = RgbaColor.FromHex("#F59E0B");          // amber
    public RgbaColor CriticalError { get; init; } = RgbaColor.FromHex("#EF4444");    // red (errors only)

    public static ChartPalette Default { get; } = new();
}
