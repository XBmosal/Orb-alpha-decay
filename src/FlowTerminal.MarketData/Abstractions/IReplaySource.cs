using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;

namespace FlowTerminal.MarketData.Abstractions;

/// <summary>A bounded request describing what recorded data to replay.</summary>
public sealed record ReplayRequest(Contract Contract, DateTime FromUtc, DateTime ToUtc);

/// <summary>
/// A deterministic source of recorded canonical events for replay. Replay uses
/// the exact same event model and downstream analytics as live data, so replayed
/// state is byte-for-byte reproducible. Replay never uses future information.
/// </summary>
public interface IReplaySource
{
    IAsyncEnumerable<MarketEvent> ReadAsync(ReplayRequest request, CancellationToken cancellationToken);
}
