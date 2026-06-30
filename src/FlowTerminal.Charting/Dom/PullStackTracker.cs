using FlowTerminal.Domain.Events;

namespace FlowTerminal.Charting.Dom;

/// <summary>
/// Tracks displayed-liquidity dynamics per price level for the advanced DOM
/// columns: <em>pulling</em> (resting size being reduced/withdrawn), <em>stacking</em>
/// (size being added), and <em>replenishment</em> (a level repeatedly consumed and
/// restored). It is observational — it measures changes in displayed size only and
/// makes no claim about intent.
///
/// Single-writer by contract: driven by one instrument's processing thread.
/// </summary>
public sealed class PullStackTracker
{
    private sealed class LevelState
    {
        public long LastSize;
        public long Pulled;          // cumulative size removed
        public long Stacked;         // cumulative size added
        public int Replenishments;   // times restored after dropping near zero
        public bool WasDepleted;
        public bool Initialized;     // first observation is a baseline, not a change
    }

    private readonly Dictionary<long, LevelState> _bid = new();
    private readonly Dictionary<long, LevelState> _ask = new();
    private readonly long _depletionThreshold;

    public PullStackTracker(long depletionThreshold = 1) => _depletionThreshold = depletionThreshold;

    public void OnDepth(in MarketEvent e)
    {
        Dictionary<long, LevelState> map;
        if (e.Type == MarketEventType.BidUpdate) map = _bid;
        else if (e.Type == MarketEventType.AskUpdate) map = _ask;
        else return;

        if (!map.TryGetValue(e.PriceTicks, out var state))
        {
            state = new LevelState { LastSize = 0 };
            map[e.PriceTicks] = state;
        }

        long newSize = Math.Max(0, e.Quantity);

        if (!state.Initialized)
        {
            // First time we see this level: record it as the baseline only.
            state.LastSize = newSize;
            state.Initialized = true;
            state.WasDepleted = newSize <= _depletionThreshold;
            return;
        }

        long delta = newSize - state.LastSize;

        if (delta < 0)
        {
            state.Pulled += -delta;
            if (newSize <= _depletionThreshold) state.WasDepleted = true;
        }
        else if (delta > 0)
        {
            state.Stacked += delta;
            if (state.WasDepleted && newSize > _depletionThreshold)
            {
                state.Replenishments++;
                state.WasDepleted = false;
            }
        }

        state.LastSize = newSize;
    }

    public long PulledAt(Side side, long priceTicks) => Map(side).TryGetValue(priceTicks, out var s) ? s.Pulled : 0;
    public long StackedAt(Side side, long priceTicks) => Map(side).TryGetValue(priceTicks, out var s) ? s.Stacked : 0;
    public int ReplenishmentsAt(Side side, long priceTicks) => Map(side).TryGetValue(priceTicks, out var s) ? s.Replenishments : 0;

    private Dictionary<long, LevelState> Map(Side side) => side == Side.Bid ? _bid : _ask;
}
