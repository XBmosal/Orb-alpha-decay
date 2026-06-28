namespace FlowTerminal.Domain.Time;

/// <summary>
/// The display time zones supported by the terminal plus the CME exchange zone
/// used for all session math. Time-zone ids are resolved in an OS-agnostic way:
/// modern .NET understands IANA ids on Windows (via ICU), but we fall back to the
/// Windows id if the IANA lookup fails on an older configuration.
/// </summary>
public static class MarketTimeZones
{
    /// <summary>CME Globex matching engine local time. All session windows are defined here.</summary>
    public static TimeZoneInfo Chicago { get; } = Resolve("America/Chicago", "Central Standard Time");

    /// <summary>Default display zone.</summary>
    public static TimeZoneInfo NewYork { get; } = Resolve("America/New_York", "Eastern Standard Time");

    public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    public static IReadOnlyList<TimeZoneInfo> DisplayZones { get; } = new[] { NewYork, Chicago, Utc };

    private static TimeZoneInfo Resolve(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }

    /// <summary>
    /// Converts an exchange-local wall time to UTC, resolving DST gaps/overlaps
    /// deterministically. Invalid local times (spring-forward gap) are pushed
    /// forward by the DST delta; ambiguous times (fall-back overlap) resolve to
    /// the standard-time (later) instant.
    /// </summary>
    public static DateTime ChicagoLocalToUtc(DateOnly date, TimeOnly time)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Unspecified);
        return LocalToUtc(local, Chicago);
    }

    public static DateTime LocalToUtc(DateTime unspecifiedLocal, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(unspecifiedLocal))
        {
            // Spring-forward gap: this wall time never occurred. Advance by the DST delta.
            var adjustment = zone.GetAdjustmentRules();
            var delta = adjustment.Length > 0 ? adjustment[0].DaylightDelta : TimeSpan.FromHours(1);
            unspecifiedLocal = unspecifiedLocal.Add(delta);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(unspecifiedLocal, DateTimeKind.Unspecified), zone);
    }
}
