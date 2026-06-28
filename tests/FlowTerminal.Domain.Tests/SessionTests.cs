using FlowTerminal.Domain.Sessions;
using FlowTerminal.Domain.Time;
using Xunit;

namespace FlowTerminal.Domain.Tests;

public class SessionTests
{
    [Fact]
    public void Rth_Window_Uses_Central_Daylight_Offset_In_Summer()
    {
        var rth = SessionTemplate.RegularTradingHours();
        var windows = rth.ResolveWindows(new DateOnly(2024, 6, 3)); // CDT (UTC-5)
        var (start, end) = windows[0];
        // 08:30 CT → 13:30 UTC, 15:00 CT → 20:00 UTC.
        Assert.Equal(new DateTime(2024, 6, 3, 13, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2024, 6, 3, 20, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void Rth_Window_Uses_Central_Standard_Offset_In_Winter()
    {
        var rth = SessionTemplate.RegularTradingHours();
        var windows = rth.ResolveWindows(new DateOnly(2024, 1, 8)); // CST (UTC-6)
        var (start, end) = windows[0];
        // 08:30 CT → 14:30 UTC, 15:00 CT → 21:00 UTC.
        Assert.Equal(new DateTime(2024, 1, 8, 14, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2024, 1, 8, 21, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void FullGlobex_Starts_Previous_Evening()
    {
        var globex = SessionTemplate.FullGlobex();
        var windows = globex.ResolveWindows(new DateOnly(2024, 6, 4));
        var (start, end) = windows[0];
        // Opens 17:00 CT on the prior day (2024-06-03 22:00 UTC), closes 16:00 CT (2024-06-04 21:00 UTC).
        Assert.Equal(new DateTime(2024, 6, 3, 22, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2024, 6, 4, 21, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void Rth_Contains_Midsession_Instant_Only_During_Window()
    {
        var rth = SessionTemplate.RegularTradingHours();
        var date = new DateOnly(2024, 6, 3);
        Assert.True(rth.Contains(new DateTime(2024, 6, 3, 15, 0, 0, DateTimeKind.Utc), date));   // 10:00 CT
        Assert.False(rth.Contains(new DateTime(2024, 6, 3, 12, 0, 0, DateTimeKind.Utc), date));  // 07:00 CT, pre-open
    }

    [Fact]
    public void TradingCalendar_Assigns_Globex_TradingDate()
    {
        var cal = new TradingCalendar();
        // 18:00 CT on 2024-06-03 (23:00 UTC) belongs to the 2024-06-04 trading date.
        var utc = new DateTime(2024, 6, 3, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2024, 6, 4), cal.TradingDate(utc));
    }

    [Fact]
    public void OpeningRange_Starts_At_Rth_Open()
    {
        var cal = new TradingCalendar();
        var (start, end) = cal.OpeningRange(new DateOnly(2024, 6, 3), TimeSpan.FromMinutes(30));
        Assert.Equal(new DateTime(2024, 6, 3, 13, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void Dst_Spring_Forward_Day_Resolves_Without_Throwing()
    {
        // 2024-03-10 is the US spring-forward day; ensure session math is well-defined.
        var rth = SessionTemplate.RegularTradingHours();
        var windows = rth.ResolveWindows(new DateOnly(2024, 3, 10));
        Assert.True(windows[0].EndUtc > windows[0].StartUtc);
    }
}
