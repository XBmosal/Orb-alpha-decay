namespace FlowTerminal.Domain.Time;

/// <summary>
/// Abstraction over "now" so that live, replay, and tests are deterministic and
/// never read the wall clock directly. Replay supplies a clock driven by event
/// timestamps; tests supply a <see cref="ManualClock"/>.
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC. All internal time is UTC.</summary>
    DateTime UtcNow { get; }
}

/// <summary>Production clock backed by the system wall clock (UTC).</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Deterministic, manually advanced clock for tests and replay. Not thread-safe
/// for concurrent advancement; intended to be driven by a single owner.
/// </summary>
public sealed class ManualClock : IClock
{
    private long _ticks;

    public ManualClock(DateTime startUtc)
    {
        if (startUtc.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Clock must be UTC.", nameof(startUtc));
        }

        _ticks = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc).Ticks;
    }

    public DateTime UtcNow => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "Time cannot move backward.");
        }

        Interlocked.Add(ref _ticks, by.Ticks);
    }

    public void SetUtc(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Clock must be UTC.", nameof(utc));
        }

        Interlocked.Exchange(ref _ticks, DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks);
    }
}
