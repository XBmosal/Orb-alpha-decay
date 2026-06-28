namespace FlowTerminal.MarketData.Resilience;

/// <summary>
/// Deterministic exponential-backoff schedule for reconnection attempts:
/// delay(n) = min(maxDelay, baseDelay × 2ⁿ). Deterministic (no random jitter) so
/// reconnection behaviour is reproducible in tests and replay. The caller resets it
/// after a successful reconnect.
/// </summary>
public sealed class ReconnectPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly int _maxAttempts;

    public ReconnectPolicy(TimeSpan? baseDelay = null, TimeSpan? maxDelay = null, int maxAttempts = 0)
    {
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        if (_baseDelay <= TimeSpan.Zero || _maxDelay < _baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay));
        }

        _maxAttempts = maxAttempts; // 0 = unlimited
    }

    public int Attempt { get; private set; }

    /// <summary>True if another attempt is allowed (always true when maxAttempts is 0/unlimited).</summary>
    public bool CanRetry => _maxAttempts == 0 || Attempt < _maxAttempts;

    /// <summary>Returns the delay before the next attempt and advances the attempt counter.</summary>
    public TimeSpan NextDelay()
    {
        // 2^Attempt without overflow: cap the shift.
        double factor = Attempt >= 30 ? double.MaxValue : 1L << Attempt;
        double ms = _baseDelay.TotalMilliseconds * factor;
        var delay = ms >= _maxDelay.TotalMilliseconds ? _maxDelay : TimeSpan.FromMilliseconds(ms);
        Attempt++;
        return delay;
    }

    public void Reset() => Attempt = 0;
}
