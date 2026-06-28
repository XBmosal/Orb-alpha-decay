using FlowTerminal.Domain.Instruments;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.MarketData.Resilience;

/// <summary>
/// Tracks the active market-data subscriptions so they can be restored after a
/// reconnect. Provider-agnostic: the Rithmic adapter records each successful
/// subscription here and replays <see cref="ForRestore"/> on reconnect.
/// </summary>
public sealed class SubscriptionRegistry
{
    private readonly Dictionary<Contract, SubscriptionOptions> _subscriptions = new();
    private readonly object _lock = new();

    public void Add(Contract contract, SubscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(options);
        lock (_lock)
        {
            _subscriptions[contract] = options;
        }
    }

    public void Remove(Contract contract)
    {
        lock (_lock)
        {
            _subscriptions.Remove(contract);
        }
    }

    public int Count
    {
        get { lock (_lock) { return _subscriptions.Count; } }
    }

    public bool IsSubscribed(Contract contract)
    {
        lock (_lock) { return _subscriptions.ContainsKey(contract); }
    }

    /// <summary>Snapshot of the subscriptions to re-issue on reconnect.</summary>
    public IReadOnlyList<(Contract Contract, SubscriptionOptions Options)> ForRestore()
    {
        lock (_lock)
        {
            var list = new List<(Contract, SubscriptionOptions)>(_subscriptions.Count);
            foreach (var kv in _subscriptions)
            {
                list.Add((kv.Key, kv.Value));
            }

            return list;
        }
    }

    public void Clear()
    {
        lock (_lock) { _subscriptions.Clear(); }
    }
}
