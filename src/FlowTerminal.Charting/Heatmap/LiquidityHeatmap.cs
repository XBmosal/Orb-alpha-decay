using FlowTerminal.Domain.Events;

namespace FlowTerminal.Charting.Heatmap;

public enum HeatmapScale
{
    Absolute,
    Percentile,
    Logarithmic,
}

/// <summary>A sealed time column: resting bid/ask size at each price (ticks) at that moment.</summary>
public sealed class HeatmapColumn
{
    public HeatmapColumn(DateTime timestampUtc) => TimestampUtc = timestampUtc;

    public DateTime TimestampUtc { get; }

    public Dictionary<long, long> Bid { get; } = new();
    public Dictionary<long, long> Ask { get; } = new();

    public long BidAt(long price) => Bid.GetValueOrDefault(price);
    public long AskAt(long price) => Ask.GetValueOrDefault(price);

    public HeatmapColumn CloneForward(DateTime ts)
    {
        // Liquidity persists until changed: carry sizes into the next column.
        var next = new HeatmapColumn(ts);
        foreach (var kv in Bid) next.Bid[kv.Key] = kv.Value;
        foreach (var kv in Ask) next.Ask[kv.Key] = kv.Value;
        return next;
    }
}

/// <summary>
/// Time×price history of displayed liquidity for the heatmap. It is built for the
/// performance constraints in the spec:
///   - A new column is sealed only when the column interval elapses — NOT per event.
///     Depth updates mutate the current column in place.
///   - Columns are grouped into fixed-size <em>tiles</em>; any change marks that
///     tile dirty so the renderer rebuilds only dirty tiles, never the whole session.
///   - A bounded ring of columns evicts the oldest, so memory stays bounded over an
///     8-hour session. Raw events live in storage; this is only the visual cache.
///
/// Bid liquidity is rendered green, ask liquidity purple (the product identity).
/// </summary>
public sealed class LiquidityHeatmap
{
    private readonly TimeSpan _columnInterval;
    private readonly int _tileColumns;
    private readonly int _maxColumns;
    private readonly List<HeatmapColumn> _columns = new();
    private readonly HashSet<int> _dirtyTiles = new();

    private int _baseIndex;            // absolute index of _columns[0]
    private DateTime _currentSealUtc;  // boundary at which the current column seals

    public LiquidityHeatmap(TimeSpan? columnInterval = null, int tileColumns = 64, int maxColumns = 4096)
    {
        if (tileColumns < 1) throw new ArgumentOutOfRangeException(nameof(tileColumns));
        if (maxColumns < tileColumns) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        _columnInterval = columnInterval ?? TimeSpan.FromMilliseconds(250);
        _tileColumns = tileColumns;
        _maxColumns = maxColumns;
    }

    public int TileColumns => _tileColumns;
    public int ColumnCount => _columns.Count;

    /// <summary>Absolute index of the first retained column (increases as old columns are evicted).</summary>
    public int BaseIndex => _baseIndex;

    public IReadOnlyList<HeatmapColumn> Columns => _columns;

    public IReadOnlyCollection<int> DirtyTiles => _dirtyTiles;

    public int TileOf(int absoluteColumnIndex) => absoluteColumnIndex / _tileColumns;

    /// <summary>Applies a depth update (best-of-book or MBP level) to the current column.</summary>
    public void OnDepth(in MarketEvent e)
    {
        if (e.Type is not (MarketEventType.BidUpdate or MarketEventType.AskUpdate))
        {
            return;
        }

        EnsureColumn(e.ExchangeTimestampUtc);
        var col = _columns[^1];
        var map = e.Type == MarketEventType.BidUpdate ? col.Bid : col.Ask;
        if (e.Quantity <= 0) map.Remove(e.PriceTicks);
        else map[e.PriceTicks] = e.Quantity;

        MarkDirty(_baseIndex + _columns.Count - 1);
    }

    /// <summary>Advances time, sealing/creating columns as interval boundaries pass.</summary>
    public void OnClock(DateTime utcNow)
    {
        EnsureColumn(utcNow);
        while (utcNow >= _currentSealUtc)
        {
            var prev = _columns[^1];
            var next = prev.CloneForward(_currentSealUtc);
            _columns.Add(next);
            _currentSealUtc += _columnInterval;
            MarkDirty(_baseIndex + _columns.Count - 1);
            Evict();
        }
    }

    private void EnsureColumn(DateTime ts)
    {
        if (_columns.Count == 0)
        {
            _columns.Add(new HeatmapColumn(ts));
            _currentSealUtc = ts + _columnInterval;
            MarkDirty(_baseIndex);
        }
    }

    private void Evict()
    {
        while (_columns.Count > _maxColumns)
        {
            _columns.RemoveAt(0);
            _baseIndex++;
        }
    }

    private void MarkDirty(int absoluteColumnIndex) => _dirtyTiles.Add(TileOf(absoluteColumnIndex));

    public void ClearDirty() => _dirtyTiles.Clear();

    /// <summary>
    /// Computes the intensity scale ceiling over a window of columns for the given
    /// scale mode. Renderers divide sizes by this to get a 0..1 brightness.
    /// </summary>
    public double ComputeScaleMax(int fromColumn, int count, HeatmapScale scale)
    {
        var sizes = new List<long>();
        int start = Math.Max(0, fromColumn - _baseIndex);
        int end = Math.Min(_columns.Count, start + count);
        for (int i = start; i < end; i++)
        {
            foreach (var v in _columns[i].Bid.Values) sizes.Add(v);
            foreach (var v in _columns[i].Ask.Values) sizes.Add(v);
        }

        if (sizes.Count == 0) return 1;

        return scale switch
        {
            HeatmapScale.Absolute => sizes.Max(),
            HeatmapScale.Logarithmic => Math.Log(sizes.Max() + 1),
            HeatmapScale.Percentile => Percentile(sizes, 0.95),
            _ => sizes.Max(),
        };
    }

    /// <summary>Normalized 0..1 intensity for a size under the given scale and ceiling.</summary>
    public static double Intensity(long size, double scaleMax, HeatmapScale scale)
    {
        if (size <= 0 || scaleMax <= 0) return 0;
        double v = scale switch
        {
            HeatmapScale.Logarithmic => Math.Log(size + 1) / scaleMax,
            _ => size / scaleMax,
        };
        return Math.Clamp(v, 0, 1);
    }

    private static double Percentile(List<long> values, double p)
    {
        values.Sort();
        int idx = (int)Math.Clamp(Math.Ceiling(p * values.Count) - 1, 0, values.Count - 1);
        return Math.Max(1, values[idx]);
    }
}
