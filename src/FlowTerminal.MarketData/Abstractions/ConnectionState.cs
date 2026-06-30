namespace FlowTerminal.MarketData.Abstractions;

/// <summary>Lifecycle state of a market-data connection, surfaced to diagnostics.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,

    /// <summary>Connected but no heartbeats/updates within the stale threshold.</summary>
    Stale,

    Faulted,
}
