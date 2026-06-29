namespace FlowTerminal.MarketData.Synthetic;

/// <summary>
/// Deterministic continuous distributions built on <see cref="DeterministicRng"/>.
/// These give the engine heavy-tailed, non-uniform draws (log-normal sizes, gaussian
/// noise) so resting depth and trade sizes are never uniform or evenly stepped.
/// The RNG is passed by ref because it is a mutable struct.
/// </summary>
internal static class SynthDistributions
{
    /// <summary>Standard-normal sample via Box–Muller (deterministic).</summary>
    public static double NextGaussian(ref DeterministicRng rng)
    {
        // Guard u1 away from 0 so Log is finite.
        double u1 = rng.NextDouble();
        if (u1 < 1e-12) u1 = 1e-12;
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Log-normal sample with the given median and shape sigma.</summary>
    public static double NextLogNormal(ref DeterministicRng rng, double median, double sigma)
    {
        double mu = Math.Log(Math.Max(1e-6, median));
        return Math.Exp(mu + sigma * NextGaussian(ref rng));
    }

    /// <summary>A heavy-tailed contract size ≥ <paramref name="floor"/>, scaled by a persistent noise factor.</summary>
    public static long Size(ref DeterministicRng rng, double median, double sigma, double noise, long floor)
    {
        double v = NextLogNormal(ref rng, median, sigma) * noise;
        long s = (long)Math.Round(v);
        return Math.Max(floor, s);
    }

    /// <summary>Persistent per-level multiplier (≈0.45..2.2) giving each level its own size identity.</summary>
    public static double NoiseFactor(ref DeterministicRng rng)
        => Math.Clamp(Math.Exp(0.35 * NextGaussian(ref rng)), 0.45, 2.4);

    public static bool Chance(ref DeterministicRng rng, double p) => rng.NextDouble() < p;

    public static double Lerp(ref DeterministicRng rng, double a, double b) => a + (b - a) * rng.NextDouble();
}
