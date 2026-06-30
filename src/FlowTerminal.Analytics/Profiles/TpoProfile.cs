using FlowTerminal.Analytics.Vwap;

namespace FlowTerminal.Analytics.Profiles;

/// <summary>
/// Time Price Opportunity (TPO) / Market Profile: divides the session into
/// fixed-duration time brackets (default 30 minutes, lettered A, B, C…) and records,
/// for each price level, which brackets traded there. POC and value area are derived
/// from TPO counts (number of brackets at a price), mirroring the volume-profile API.
/// Sessions (RTH/ETH) are handled by constructing separate profiles per window.
/// </summary>
public sealed class TpoProfile
{
    private readonly DateTime _sessionStartUtc;
    private readonly TimeSpan _bracket;
    private readonly Dictionary<long, HashSet<int>> _brackets = new();
    private int _maxBracket = -1;

    public TpoProfile(DateTime sessionStartUtc, TimeSpan? bracketDuration = null)
    {
        _sessionStartUtc = sessionStartUtc;
        _bracket = bracketDuration ?? TimeSpan.FromMinutes(30);
        if (_bracket <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bracketDuration));
    }

    public int BracketCount => _maxBracket + 1;

    public void AddTrade(long priceTicks, DateTime utc)
    {
        if (utc < _sessionStartUtc) return;
        int bracket = (int)((utc - _sessionStartUtc).Ticks / _bracket.Ticks);
        if (bracket > _maxBracket) _maxBracket = bracket;
        if (!_brackets.TryGetValue(priceTicks, out var set))
        {
            set = new HashSet<int>();
            _brackets[priceTicks] = set;
        }

        set.Add(bracket);
    }

    /// <summary>Number of distinct brackets (TPOs) that traded at a price.</summary>
    public int TpoCountAt(long priceTicks) => _brackets.TryGetValue(priceTicks, out var s) ? s.Count : 0;

    /// <summary>The lettered brackets at a price, e.g. "ABD".</summary>
    public string LettersAt(long priceTicks)
    {
        if (!_brackets.TryGetValue(priceTicks, out var s)) return string.Empty;
        var ordered = s.OrderBy(b => b).Select(b => (char)('A' + (b % 26)));
        return new string(ordered.ToArray());
    }

    public long PocTicks()
    {
        long best = long.MinValue;
        int bestCount = -1;
        foreach (var kv in _brackets)
        {
            if (kv.Value.Count > bestCount || (kv.Value.Count == bestCount && kv.Key > best))
            {
                bestCount = kv.Value.Count;
                best = kv.Key;
            }
        }

        return best;
    }

    public ValueArea ComputeValueArea(double percent = 0.70)
    {
        if (_brackets.Count == 0) return new ValueArea(long.MinValue, long.MinValue, long.MinValue);

        var prices = _brackets.Keys.OrderBy(p => p).ToList();
        long total = _brackets.Values.Sum(s => s.Count);
        long poc = PocTicks();
        int pocIdx = prices.IndexOf(poc);

        long target = (long)Math.Ceiling(total * percent);
        long acc = _brackets[poc].Count;
        int lo = pocIdx, hi = pocIdx;
        while (acc < target && (lo > 0 || hi < prices.Count - 1))
        {
            long below = lo > 0 ? _brackets[prices[lo - 1]].Count : -1;
            long above = hi < prices.Count - 1 ? _brackets[prices[hi + 1]].Count : -1;
            if (above >= below) { hi++; acc += _brackets[prices[hi]].Count; }
            else { lo--; acc += _brackets[prices[lo]].Count; }
        }

        return new ValueArea(poc, prices[lo], prices[hi]);
    }
}
