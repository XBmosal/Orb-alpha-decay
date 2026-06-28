namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// CME quarterly delivery months for equity-index futures and their standard
/// single-letter futures month codes. Equity index futures (NQ, ES) trade only
/// on the March quarterly cycle.
/// </summary>
public enum QuarterlyMonth
{
    March = 3,
    June = 6,
    September = 9,
    December = 12,
}

public static class QuarterlyMonthExtensions
{
    /// <summary>Single-letter futures month code (H, M, U, Z).</summary>
    public static char Code(this QuarterlyMonth month) => month switch
    {
        QuarterlyMonth.March => 'H',
        QuarterlyMonth.June => 'M',
        QuarterlyMonth.September => 'U',
        QuarterlyMonth.December => 'Z',
        _ => throw new ArgumentOutOfRangeException(nameof(month), month, null),
    };

    public static bool TryFromCode(char code, out QuarterlyMonth month)
    {
        switch (char.ToUpperInvariant(code))
        {
            case 'H': month = QuarterlyMonth.March; return true;
            case 'M': month = QuarterlyMonth.June; return true;
            case 'U': month = QuarterlyMonth.September; return true;
            case 'Z': month = QuarterlyMonth.December; return true;
            default: month = default; return false;
        }
    }

    public static int CalendarMonth(this QuarterlyMonth month) => (int)month;
}
