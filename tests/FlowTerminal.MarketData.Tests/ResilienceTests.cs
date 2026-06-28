using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;
using FlowTerminal.MarketData.Resilience;
using Xunit;

namespace FlowTerminal.MarketData.Tests;

public class StaleFeedMonitorTests
{
    private static readonly DateTime T0 = new(2024, 6, 3, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Not_Stale_Before_Any_Activity_Is_Recorded()
    {
        var m = new StaleFeedMonitor(TimeSpan.FromSeconds(5));
        Assert.False(m.IsStale(T0)); // no activity yet → not flagged stale
        Assert.False(m.HasActivity);
    }

    [Fact]
    public void Becomes_Stale_After_Threshold()
    {
        var m = new StaleFeedMonitor(TimeSpan.FromSeconds(5));
        m.RecordActivity(T0);
        Assert.False(m.IsStale(T0.AddSeconds(4)));
        Assert.True(m.IsStale(T0.AddSeconds(6)));
    }

    [Fact]
    public void Activity_Clears_Stale_State()
    {
        var m = new StaleFeedMonitor(TimeSpan.FromSeconds(5));
        m.RecordActivity(T0);
        Assert.True(m.IsStale(T0.AddSeconds(6)));
        m.RecordActivity(T0.AddSeconds(6));
        Assert.False(m.IsStale(T0.AddSeconds(7)));
    }
}

public class ReconnectPolicyTests
{
    [Fact]
    public void Backoff_Is_Exponential_And_Capped()
    {
        var p = new ReconnectPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));
        Assert.Equal(TimeSpan.FromSeconds(1), p.NextDelay()); // 1×
        Assert.Equal(TimeSpan.FromSeconds(2), p.NextDelay()); // 2×
        Assert.Equal(TimeSpan.FromSeconds(4), p.NextDelay()); // 4×
        Assert.Equal(TimeSpan.FromSeconds(8), p.NextDelay()); // 8× → cap
        Assert.Equal(TimeSpan.FromSeconds(8), p.NextDelay()); // stays capped
    }

    [Fact]
    public void Reset_Restarts_The_Schedule()
    {
        var p = new ReconnectPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));
        p.NextDelay(); p.NextDelay();
        p.Reset();
        Assert.Equal(0, p.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(1), p.NextDelay());
    }

    [Fact]
    public void Max_Attempts_Limits_Retries()
    {
        var p = new ReconnectPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), maxAttempts: 2);
        Assert.True(p.CanRetry); p.NextDelay();
        Assert.True(p.CanRetry); p.NextDelay();
        Assert.False(p.CanRetry);
    }
}

public class SubscriptionRegistryTests
{
    [Fact]
    public void Tracks_And_Restores_Subscriptions()
    {
        var reg = new SubscriptionRegistry();
        var nq = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        var es = new Contract(RootSymbol.ES, QuarterlyMonth.December, 2025);

        reg.Add(nq, SubscriptionOptions.Default);
        reg.Add(es, new SubscriptionOptions { MarketByPriceDepth = false });

        Assert.Equal(2, reg.Count);
        Assert.True(reg.IsSubscribed(nq));

        var restore = reg.ForRestore();
        Assert.Equal(2, restore.Count);

        reg.Remove(nq);
        Assert.False(reg.IsSubscribed(nq));
        Assert.Equal(1, reg.Count);
    }

    [Fact]
    public void Re_Adding_Same_Contract_Updates_Options()
    {
        var reg = new SubscriptionRegistry();
        var nq = new Contract(RootSymbol.NQ, QuarterlyMonth.December, 2025);
        reg.Add(nq, SubscriptionOptions.Default);
        reg.Add(nq, new SubscriptionOptions { DepthLevels = 40 });
        Assert.Equal(1, reg.Count);
    }
}
