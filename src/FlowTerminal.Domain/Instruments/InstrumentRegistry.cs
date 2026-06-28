namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// Canonical source of truth for the two supported instrument specifications.
/// Specs match published CME contract specifications:
///   NQ: tick 0.25 pt, point value $20, tick value $5.00
///   ES: tick 0.25 pt, point value $50, tick value $12.50
/// </summary>
public static class InstrumentRegistry
{
    public static InstrumentSpec NQ { get; } = new(
        root: RootSymbol.NQ,
        rootSymbol: "NQ",
        exchange: "CME Globex",
        tickSize: 0.25m,
        pointValue: 20m,
        currency: "USD",
        description: "E-mini Nasdaq-100");

    public static InstrumentSpec ES { get; } = new(
        root: RootSymbol.ES,
        rootSymbol: "ES",
        exchange: "CME Globex",
        tickSize: 0.25m,
        pointValue: 50m,
        currency: "USD",
        description: "E-mini S&P 500");

    private static readonly IReadOnlyDictionary<RootSymbol, InstrumentSpec> ByRoot =
        new Dictionary<RootSymbol, InstrumentSpec>
        {
            [RootSymbol.NQ] = NQ,
            [RootSymbol.ES] = ES,
        };

    /// <summary>The complete, ordered set of instruments the UI may expose.</summary>
    public static IReadOnlyList<InstrumentSpec> All { get; } = new[] { NQ, ES };

    public static InstrumentSpec Get(RootSymbol root) =>
        ByRoot.TryGetValue(root, out var spec)
            ? spec
            : throw new ArgumentOutOfRangeException(nameof(root), root, "Unsupported root symbol. Only NQ and ES are exposed.");

    public static bool TryGet(RootSymbol root, out InstrumentSpec spec) => ByRoot.TryGetValue(root, out spec!);

    /// <summary>Resolves a root symbol from its exchange string ("NQ"/"ES"), case-insensitive.</summary>
    public static bool TryParseRoot(string? rootSymbol, out RootSymbol root)
    {
        switch (rootSymbol?.Trim().ToUpperInvariant())
        {
            case "NQ":
                root = RootSymbol.NQ;
                return true;
            case "ES":
                root = RootSymbol.ES;
                return true;
            default:
                root = default;
                return false;
        }
    }
}
