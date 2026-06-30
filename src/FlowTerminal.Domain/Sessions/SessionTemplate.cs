using FlowTerminal.Domain.Time;

namespace FlowTerminal.Domain.Sessions;

public enum SessionKind
{
    FullGlobex,
    RegularTradingHours,
    ExtendedTradingHours,
    Overnight,
    Custom,
}

/// <summary>
/// One contiguous time window within a trading day, expressed in CME exchange
/// local time (America/Chicago). <see cref="StartDayOffset"/> allows a window to
/// begin on the previous calendar day, which is how the Globex day is modelled
/// (it opens at 17:00 the prior evening).
/// </summary>
public readonly record struct SessionSegment(TimeOnly Start, int StartDayOffset, TimeOnly End, int EndDayOffset);

/// <summary>
/// A named, DST-aware session definition. All windows are defined in exchange
/// local time and converted to UTC per trading date, so session-dependent
/// calculations stay correct across DST transitions, partial sessions, and
/// weekends. Bars are never grouped by raw local calendar date.
/// </summary>
public sealed class SessionTemplate
{
    private readonly SessionSegment[] _segments;

    public SessionTemplate(string name, SessionKind kind, IReadOnlyList<SessionSegment> segments)
    {
        if (segments is null || segments.Count == 0)
        {
            throw new ArgumentException("A session template requires at least one segment.", nameof(segments));
        }

        Name = name;
        Kind = kind;
        _segments = segments.ToArray();
    }

    public string Name { get; }

    public SessionKind Kind { get; }

    public IReadOnlyList<SessionSegment> Segments => _segments;

    /// <summary>Resolves the concrete UTC windows for a given trading date.</summary>
    public IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> ResolveWindows(DateOnly tradingDate)
    {
        var result = new (DateTime, DateTime)[_segments.Length];
        for (int i = 0; i < _segments.Length; i++)
        {
            var seg = _segments[i];
            var startDate = tradingDate.AddDays(seg.StartDayOffset);
            var endDate = tradingDate.AddDays(seg.EndDayOffset);
            var startUtc = MarketTimeZones.ChicagoLocalToUtc(startDate, seg.Start);
            var endUtc = MarketTimeZones.ChicagoLocalToUtc(endDate, seg.End);
            result[i] = (startUtc, endUtc);
        }

        return result;
    }

    /// <summary>True if the UTC instant falls within any window of the given trading date.</summary>
    public bool Contains(DateTime utc, DateOnly tradingDate)
    {
        foreach (var (startUtc, endUtc) in ResolveWindows(tradingDate))
        {
            if (utc >= startUtc && utc < endUtc)
            {
                return true;
            }
        }

        return false;
    }

    // ---- Standard CME equity-index templates ----

    /// <summary>
    /// Full Globex session: 17:00 CT the prior evening through 16:00 CT
    /// (single continuous window; the brief daily maintenance break is treated as
    /// the boundary between trading days).
    /// </summary>
    public static SessionTemplate FullGlobex() => new(
        "Full CME Globex", SessionKind.FullGlobex,
        new[] { new SessionSegment(new TimeOnly(17, 0), -1, new TimeOnly(16, 0), 0) });

    /// <summary>Regular Trading Hours: 08:30–15:00 CT.</summary>
    public static SessionTemplate RegularTradingHours() => new(
        "Regular Trading Hours", SessionKind.RegularTradingHours,
        new[] { new SessionSegment(new TimeOnly(8, 30), 0, new TimeOnly(15, 0), 0) });

    /// <summary>
    /// Extended Trading Hours: the Globex session outside RTH, modelled as two
    /// windows (overnight pre-open and the post-RTH close-out).
    /// </summary>
    public static SessionTemplate ExtendedTradingHours() => new(
        "Extended Trading Hours", SessionKind.ExtendedTradingHours,
        new[]
        {
            new SessionSegment(new TimeOnly(17, 0), -1, new TimeOnly(8, 30), 0),
            new SessionSegment(new TimeOnly(15, 0), 0, new TimeOnly(16, 0), 0),
        });

    /// <summary>Overnight session: 17:00 CT the prior evening through the 08:30 CT RTH open.</summary>
    public static SessionTemplate Overnight() => new(
        "Overnight", SessionKind.Overnight,
        new[] { new SessionSegment(new TimeOnly(17, 0), -1, new TimeOnly(8, 30), 0) });

    /// <summary>Builds a custom single-window session in exchange local time.</summary>
    public static SessionTemplate Custom(string name, TimeOnly start, int startDayOffset, TimeOnly end, int endDayOffset) =>
        new(name, SessionKind.Custom, new[] { new SessionSegment(start, startDayOffset, end, endDayOffset) });
}
