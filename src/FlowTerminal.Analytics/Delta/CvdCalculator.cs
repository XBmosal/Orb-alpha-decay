using FlowTerminal.Domain.Events;

namespace FlowTerminal.Analytics.Delta;

public readonly record struct CvdPoint(DateTime TimestampUtc, long CumulativeDelta);

/// <summary>
/// Incremental cumulative volume delta. Each aggressive buy adds its size; each
/// aggressive sell subtracts it; unknown-aggressor trades do not move CVD. The
/// running value is maintained in O(1) per trade — no full recomputation.
/// </summary>
public sealed class CvdCalculator
{
    private long _cumulativeDelta;
    private long _maxPositive;
    private long _maxNegative;

    public long CumulativeDelta => _cumulativeDelta;

    /// <summary>Highest CVD reached so far (session max positive excursion).</summary>
    public long MaxPositive => _maxPositive;

    /// <summary>Lowest CVD reached so far (session max negative excursion).</summary>
    public long MaxNegative => _maxNegative;

    public CvdPoint Add(in MarketEvent trade)
    {
        if (trade.Type == MarketEventType.Trade && trade.Quantity > 0)
        {
            switch (trade.Aggressor)
            {
                case AggressorSide.Buy:
                    _cumulativeDelta += trade.Quantity;
                    break;
                case AggressorSide.Sell:
                    _cumulativeDelta -= trade.Quantity;
                    break;
            }

            if (_cumulativeDelta > _maxPositive)
            {
                _maxPositive = _cumulativeDelta;
            }

            if (_cumulativeDelta < _maxNegative)
            {
                _maxNegative = _cumulativeDelta;
            }
        }

        return new CvdPoint(trade.ExchangeTimestampUtc, _cumulativeDelta);
    }

    public void Reset()
    {
        _cumulativeDelta = 0;
        _maxPositive = 0;
        _maxNegative = 0;
    }
}
