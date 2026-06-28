namespace FlowTerminal.Domain.Instruments;

/// <summary>
/// A specific quarterly futures contract for NQ or ES, e.g. NQZ5 (Dec 2025).
///
/// Contract symbols are never hard-coded anywhere in the app: they are either
/// discovered from reference data or constructed here from a root + month + year.
/// The expiration date used for rollover hints is the standard CME equity-index
/// final settlement date (the third Friday of the contract month). When authorized
/// reference data supplies an exact expiration, that value should be preferred and
/// passed to <see cref="WithExpiration"/>.
/// </summary>
public sealed class Contract : IEquatable<Contract>
{
    public Contract(RootSymbol root, QuarterlyMonth month, int year, DateOnly? expirationDateUtc = null)
    {
        if (year < 2000 || year > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year out of supported range.");
        }

        Root = root;
        Month = month;
        Year = year;
        ExpirationDateUtc = expirationDateUtc ?? StandardExpiration(month, year);
        Spec = InstrumentRegistry.Get(root);
    }

    public RootSymbol Root { get; }

    public QuarterlyMonth Month { get; }

    public int Year { get; }

    public InstrumentSpec Spec { get; }

    /// <summary>Final settlement date used for rollover hints (UTC calendar date).</summary>
    public DateOnly ExpirationDateUtc { get; }

    /// <summary>Short single-digit-year symbol, e.g. "NQZ5".</summary>
    public string Symbol => $"{Spec.RootSymbol}{Month.Code()}{Year % 10}";

    /// <summary>Unambiguous two-digit-year symbol, e.g. "NQZ25".</summary>
    public string FullSymbol => $"{Spec.RootSymbol}{Month.Code()}{Year % 100:D2}";

    public Contract WithExpiration(DateOnly expirationDateUtc) =>
        new(Root, Month, Year, expirationDateUtc);

    public int DaysUntilExpiration(DateOnly today) => ExpirationDateUtc.DayNumber - today.DayNumber;

    /// <summary>
    /// Standard CME equity-index final settlement date: the third Friday of the
    /// contract month. Used only as a default when reference data is unavailable.
    /// </summary>
    public static DateOnly StandardExpiration(QuarterlyMonth month, int year)
    {
        int m = month.CalendarMonth();
        var first = new DateOnly(year, m, 1);
        // Days until the first Friday (DayOfWeek.Friday == 5).
        int offsetToFirstFriday = ((int)DayOfWeek.Friday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offsetToFirstFriday + 14); // first Friday + 2 weeks = third Friday
    }

    public static bool TryParse(string symbol, out Contract? contract)
    {
        contract = null;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        symbol = symbol.Trim().ToUpperInvariant();
        // Root is the leading alphabetic run; remainder is monthCode + year digits.
        int i = 0;
        while (i < symbol.Length && char.IsLetter(symbol[i]) && i < 2)
        {
            i++;
        }

        if (i < 1 || i >= symbol.Length)
        {
            return false;
        }

        string rootPart = symbol[..i];
        char monthCode = symbol[i];
        string yearPart = symbol[(i + 1)..];

        if (!InstrumentRegistry.TryParseRoot(rootPart, out var root) ||
            !QuarterlyMonthExtensions.TryFromCode(monthCode, out var month) ||
            !int.TryParse(yearPart, out int yy))
        {
            return false;
        }

        int year = yearPart.Length switch
        {
            1 => 2020 + yy,         // single digit assumes current decade (2020s)
            2 => 2000 + yy,
            4 => yy,
            _ => -1,
        };

        if (year < 0)
        {
            return false;
        }

        contract = new Contract(root, month, year);
        return true;
    }

    public bool Equals(Contract? other) =>
        other is not null && Root == other.Root && Month == other.Month && Year == other.Year;

    public override bool Equals(object? obj) => Equals(obj as Contract);

    public override int GetHashCode() => HashCode.Combine(Root, Month, Year);

    public override string ToString() => FullSymbol;
}
