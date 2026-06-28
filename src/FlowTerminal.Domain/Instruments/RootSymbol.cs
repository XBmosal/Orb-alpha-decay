namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// The only instrument families this terminal exposes. The UI is intentionally
/// limited to NQ and ES. The internal model is extensible, but micro contracts
/// (MNQ/MES), equities, options, crypto, and FX are deliberately not represented.
/// </summary>
public enum RootSymbol
{
    /// <summary>E-mini Nasdaq-100 futures (CME Globex).</summary>
    NQ = 1,

    /// <summary>E-mini S&amp;P 500 futures (CME Globex).</summary>
    ES = 2,
}
