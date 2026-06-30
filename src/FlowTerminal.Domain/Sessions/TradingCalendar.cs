using FlowTerminal.Domain.Time;

namespace FlowTerminal.Domain.Sessions;

/// <summary>
/// Maps UTC instants to CME trading dates and derives session-relative windows
/// (opening range, initial balance). The Globex convention is used: trading day
/// <c>D</c> runs from 17:00 CT on <c>D-1</c> to 16:00 CT on <c>D</c>, so an instant
/// at or after 17:00 CT belongs to the next calendar day's trading date.
/// </summary>
public sealed class TradingCalendar
{
    private static readonly TimeOnly GlobexRollover = new(17, 0);

    /// <summary>The CME trading date that a UTC instant belongs to.</summary>
    public DateOnly TradingDate(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MarketTimeZones.Chicago);
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        if (time >= GlobexRollover)
        {
            date = date.AddDays(1);
        }

        // Weekend rollover: trades posted Sunday evening belong to Monday's trading date.
        if (date.DayOfWeek == DayOfWeek.Saturday)
        {
            date = date.AddDays(2);
        }

        return date;
    }

    public bool IsWeekendClosure(DateOnly tradingDate) =>
        tradingDate.DayOfWeek == DayOfWeek.Saturday;

    /// <summary>
    /// The opening-range window for a trading date: <paramref name="length"/> starting
    /// at the RTH open (08:30 CT). Used by opening-range and IB analytics.
    /// </summary>
    public (DateTime StartUtc, DateTime EndUtc) OpeningRange(DateOnly tradingDate, TimeSpan length)
    {
        if (length <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var startUtc = MarketTimeZones.ChicagoLocalToUtc(tradingDate, new TimeOnly(8, 30));
        return (startUtc, startUtc + length);
    }

    /// <summary>Initial-balance window: the first <paramref name="length"/> (default 60 min) of RTH.</summary>
    public (DateTime StartUtc, DateTime EndUtc) InitialBalance(DateOnly tradingDate, TimeSpan? length = null) =>
        OpeningRange(tradingDate, length ?? TimeSpan.FromMinutes(60));
}
