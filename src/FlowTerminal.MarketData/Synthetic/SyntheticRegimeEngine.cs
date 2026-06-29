namespace FlowTerminal.MarketData.Synthetic;

/// <summary>The eight synthetic market regimes the session moves between.</summary>
public enum SyntheticRegime
{
    Quiet,
    Balanced,
    TrendingUp,
    TrendingDown,
    Volatile,
    LiquidityVacuum,
    Absorption,
    FastMarket,
}

/// <summary>
/// Immutable bundle of multipliers a regime imposes on the order-flow engine.
/// The engine reads these every step; no behaviour is hard-coded per regime
/// outside this profile, so adding/tuning a regime is a local change.
/// </summary>
public readonly record struct RegimeProfile(
    SyntheticRegime Regime,
    double DepthMult,        // scales resting size
    double TradeMult,        // scales aggressive trade frequency
    double BuyBias,          // -1..+1 directional pressure of aggressors
    double CancelMult,       // scales pulling
    double ReplenishMult,    // scales refill behind executions (absorption ≫ 1)
    double SpreadWiden,      // probability of a wider-than-1-tick spread
    double SweepMult,        // scales large-order / sweep propensity
    double StepScale);       // scales wall-clock advance per step (fast market < 1)

/// <summary>
/// A small stochastic state machine over <see cref="SyntheticRegime"/>. Each regime
/// lasts a variable number of steps and transitions via a weighted table, so the
/// session evolves through quiet/balanced/trending/volatile/absorption/vacuum phases
/// rather than sitting in one mode. Fully deterministic given the shared RNG.
/// </summary>
public sealed class SyntheticRegimeEngine
{
    private SyntheticRegime _current = SyntheticRegime.Balanced;
    private int _remaining;
    private int _stepsInRegime;

    public SyntheticRegime Current => _current;
    public int StepsInRegime => _stepsInRegime;

    public SyntheticRegimeEngine(ref DeterministicRng rng)
    {
        _remaining = DurationFor(_current, ref rng);
    }

    /// <summary>Advances one step. Returns true on the step where the regime changed.</summary>
    public bool Step(ref DeterministicRng rng)
    {
        _stepsInRegime++;
        if (--_remaining > 0)
        {
            return false;
        }

        _current = NextRegime(_current, ref rng);
        _remaining = DurationFor(_current, ref rng);
        _stepsInRegime = 0;
        return true;
    }

    public RegimeProfile Profile() => _current switch
    {
        SyntheticRegime.Quiet =>
            new(_current, DepthMult: 1.15, TradeMult: 0.35, BuyBias: 0.0,
                CancelMult: 0.7, ReplenishMult: 1.1, SpreadWiden: 0.12, SweepMult: 0.4, StepScale: 1.5),
        SyntheticRegime.Balanced =>
            new(_current, 1.0, 1.0, 0.0, 1.0, 1.0, 0.06, 1.0, 1.0),
        SyntheticRegime.TrendingUp =>
            new(_current, 0.9, 1.2, +0.42, 1.15, 0.9, 0.08, 1.3, 0.85),
        SyntheticRegime.TrendingDown =>
            new(_current, 0.9, 1.2, -0.42, 1.15, 0.9, 0.08, 1.3, 0.85),
        SyntheticRegime.Volatile =>
            new(_current, 0.8, 1.5, 0.0, 1.5, 0.8, 0.22, 1.8, 0.7),
        SyntheticRegime.LiquidityVacuum =>
            new(_current, 0.4, 1.1, 0.0, 1.9, 0.35, 0.4, 1.5, 0.7),
        SyntheticRegime.Absorption =>
            new(_current, 1.5, 1.6, 0.0, 0.6, 3.2, 0.05, 1.1, 0.9),
        SyntheticRegime.FastMarket =>
            new(_current, 0.85, 2.2, 0.0, 1.4, 1.0, 0.18, 2.2, 0.5),
        _ => new(_current, 1, 1, 0, 1, 1, 0.06, 1, 1),
    };

    // Variable durations (in steps): quiet/balanced last longer than spikes.
    private static int DurationFor(SyntheticRegime r, ref DeterministicRng rng) => r switch
    {
        SyntheticRegime.Quiet => rng.NextInt(900, 2600),
        SyntheticRegime.Balanced => rng.NextInt(1200, 3200),
        SyntheticRegime.TrendingUp or SyntheticRegime.TrendingDown => rng.NextInt(700, 2200),
        SyntheticRegime.Volatile => rng.NextInt(300, 1100),
        SyntheticRegime.LiquidityVacuum => rng.NextInt(120, 520),
        SyntheticRegime.Absorption => rng.NextInt(260, 900),
        SyntheticRegime.FastMarket => rng.NextInt(160, 700),
        _ => rng.NextInt(600, 1800),
    };

    // Weighted transitions: most paths route through Balanced, trends can persist
    // or flip, spikes resolve back to calmer states.
    private static SyntheticRegime NextRegime(SyntheticRegime from, ref DeterministicRng rng)
    {
        ReadOnlySpan<(SyntheticRegime To, int W)> table = from switch
        {
            SyntheticRegime.Quiet => new[]
            {
                (SyntheticRegime.Balanced, 55), (SyntheticRegime.TrendingUp, 12),
                (SyntheticRegime.TrendingDown, 12), (SyntheticRegime.Absorption, 11),
                (SyntheticRegime.Volatile, 10),
            },
            SyntheticRegime.Balanced => new[]
            {
                (SyntheticRegime.Quiet, 22), (SyntheticRegime.TrendingUp, 18),
                (SyntheticRegime.TrendingDown, 18), (SyntheticRegime.Volatile, 14),
                (SyntheticRegime.Absorption, 12), (SyntheticRegime.LiquidityVacuum, 8),
                (SyntheticRegime.FastMarket, 8),
            },
            SyntheticRegime.TrendingUp => new[]
            {
                (SyntheticRegime.Balanced, 38), (SyntheticRegime.TrendingUp, 20),
                (SyntheticRegime.Volatile, 14), (SyntheticRegime.FastMarket, 12),
                (SyntheticRegime.Absorption, 10), (SyntheticRegime.TrendingDown, 6),
            },
            SyntheticRegime.TrendingDown => new[]
            {
                (SyntheticRegime.Balanced, 38), (SyntheticRegime.TrendingDown, 20),
                (SyntheticRegime.Volatile, 14), (SyntheticRegime.FastMarket, 12),
                (SyntheticRegime.Absorption, 10), (SyntheticRegime.TrendingUp, 6),
            },
            SyntheticRegime.Volatile => new[]
            {
                (SyntheticRegime.Balanced, 40), (SyntheticRegime.LiquidityVacuum, 16),
                (SyntheticRegime.FastMarket, 16), (SyntheticRegime.TrendingUp, 12),
                (SyntheticRegime.TrendingDown, 12), (SyntheticRegime.Quiet, 4),
            },
            SyntheticRegime.LiquidityVacuum => new[]
            {
                (SyntheticRegime.Volatile, 34), (SyntheticRegime.Balanced, 30),
                (SyntheticRegime.FastMarket, 20), (SyntheticRegime.Absorption, 16),
            },
            SyntheticRegime.Absorption => new[]
            {
                (SyntheticRegime.Balanced, 44), (SyntheticRegime.TrendingUp, 16),
                (SyntheticRegime.TrendingDown, 16), (SyntheticRegime.Quiet, 14),
                (SyntheticRegime.Volatile, 10),
            },
            SyntheticRegime.FastMarket => new[]
            {
                (SyntheticRegime.Volatile, 36), (SyntheticRegime.Balanced, 30),
                (SyntheticRegime.TrendingUp, 14), (SyntheticRegime.TrendingDown, 14),
                (SyntheticRegime.LiquidityVacuum, 6),
            },
            _ => new[] { (SyntheticRegime.Balanced, 1) },
        };

        int total = 0;
        foreach (var (_, w) in table) total += w;
        int roll = rng.NextInt(total);
        foreach (var (to, w) in table)
        {
            if (roll < w) return to;
            roll -= w;
        }

        return SyntheticRegime.Balanced;
    }
}
