using System.Globalization;

namespace FlowTerminal.Analytics.Vwap;

/// <summary>
/// Daily / weekly / monthly / anchored VWAP in one place. Each band is a
/// <see cref="VwapCalculator"/> that resets when its period rolls over; the anchored
/// band only resets when <see cref="Anchor"/> is called. Periods are keyed off the
/// trading date supplied with each trade so they align with the session model.
/// </summary>
public sealed class MultiVwap
{
    private readonly VwapCalculator _daily = new();
    private readonly VwapCalculator _weekly = new();
    private readonly VwapCalculator _monthly = new();
    private readonly VwapCalculator _anchored = new();

    private DateOnly? _lastDay;
    private (int Year, int Week)? _lastWeek;
    private (int Year, int Month)? _lastMonth;

    public VwapValue Daily => _daily.Value();
    public VwapValue Weekly => _weekly.Value();
    public VwapValue Monthly => _monthly.Value();
    public VwapValue Anchored => _anchored.Value();
    public bool HasAnchor { get; private set; }

    public void AddTrade(long priceTicks, long quantity, DateOnly tradingDate)
    {
        if (quantity <= 0) return;

        if (_lastDay != tradingDate) { _daily.Reset(); _lastDay = tradingDate; }

        var week = IsoWeek(tradingDate);
        if (_lastWeek != week) { _weekly.Reset(); _lastWeek = week; }

        var month = (tradingDate.Year, tradingDate.Month);
        if (_lastMonth != month) { _monthly.Reset(); _lastMonth = month; }

        _daily.AddAt(priceTicks, quantity);
        _weekly.AddAt(priceTicks, quantity);
        _monthly.AddAt(priceTicks, quantity);
        if (HasAnchor) _anchored.AddAt(priceTicks, quantity);
    }

    /// <summary>Starts (or restarts) the anchored VWAP from now; subsequent trades accumulate into it.</summary>
    public void Anchor()
    {
        _anchored.Reset();
        HasAnchor = true;
    }

    private static (int, int) IsoWeek(DateOnly d)
    {
        var dt = d.ToDateTime(TimeOnly.MinValue);
        int week = ISOWeek.GetWeekOfYear(dt);
        int year = ISOWeek.GetYear(dt);
        return (year, week);
    }
}
