using System.Runtime.CompilerServices;
using FlowTerminal.Domain.Events;
using FlowTerminal.MarketData.Abstractions;

namespace FlowTerminal.Replay;

/// <summary>
/// Replays an in-order sequence of recorded canonical events. Phase 1 provides an
/// in-memory source (used by integration tests and to prove the replay contract);
/// Phase 5 adds the Parquet-backed source and checkpoint seeking. Replay always
/// uses the same event model and downstream analytics as live data and never uses
/// future information.
/// </summary>
public sealed class InMemoryReplaySource : IReplaySource
{
    private readonly IReadOnlyList<MarketEvent> _events;

    public InMemoryReplaySource(IReadOnlyList<MarketEvent> events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public async IAsyncEnumerable<MarketEvent> ReadAsync(
        ReplayRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var ev in _events)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (ev.ExchangeTimestampUtc < request.FromUtc || ev.ExchangeTimestampUtc >= request.ToUtc)
            {
                continue;
            }

            yield return ev;
            await Task.Yield();
        }
    }
}
