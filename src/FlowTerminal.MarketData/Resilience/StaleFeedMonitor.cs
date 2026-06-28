namespace FlowTerminal.MarketData.Resilience;

/// <summary>
/// Detects a stale feed: a connection that is nominally up but has stopped
/// delivering updates/heartbeats within a threshold. Provider-agnostic — driven by
/// the timestamps of incoming activity, so it works for mock, replay, and the
/// authorized Rithmic adapter alike.
/// </summary>
public sealed class StaleFeedMonitor
{
    private readonly TimeSpan _threshold;
    private DateTime _lastActivityUtc;
    private bool _hasActivity;

    public StaleFeedMonitor(TimeSpan threshold)
    {
        if (threshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }

        _threshold = threshold;
    }

    /// <summary>Records that the feed delivered something (an event or a heartbeat) at this time.</summary>
    public void RecordActivity(DateTime utcNow)
    {
        _lastActivityUtc = utcNow;
        _hasActivity = true;
    }

    public bool HasActivity => _hasActivity;

    public TimeSpan SinceLastActivity(DateTime utcNow) =>
        _hasActivity ? utcNow - _lastActivityUtc : TimeSpan.MaxValue;

    /// <summary>True once activity has been seen and the gap since exceeds the threshold.</summary>
    public bool IsStale(DateTime utcNow) =>
        _hasActivity && (utcNow - _lastActivityUtc) > _threshold;

    public void Reset()
    {
        _hasActivity = false;
        _lastActivityUtc = default;
    }
}
