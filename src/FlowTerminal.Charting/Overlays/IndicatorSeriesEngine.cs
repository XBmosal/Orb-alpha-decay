using FlowTerminal.Analytics.Bars;
using FlowTerminal.Analytics.Indicators;

namespace FlowTerminal.Charting.Overlays;

/// <summary>
/// Turns the visible bar list plus the set of enabled indicator short-codes into
/// render-ready <see cref="IndicatorRenderData"/>. Each enabled indicator is replayed
/// over the bars with a fresh, deterministic calculator and its output recorded per
/// bar — so the result is a pure function of the bars and settings, identical between
/// live and replay, with no cross-frame state to drift or repaint. This runs on the UI
/// sampling path (once per frame), never on the market-data event thread.
///
/// Indicator short-codes (used by the Indicators menu and persisted in templates):
///   Overlays: MA, BB (Bollinger), DC (Donchian), KC (Keltner)
///   Panes:    RSI, MACD, ADX, ATR, STOCH, CCI, ROC, MOM
/// </summary>
public sealed class IndicatorSeriesEngine
{
    public static readonly IReadOnlyList<string> OverlayCodes = new[] { "MA", "BB", "DC", "KC" };
    public static readonly IReadOnlyList<string> OscillatorCodes = new[] { "RSI", "MACD", "ADX", "ATR", "STOCH", "CCI", "ROC", "MOM" };

    private readonly ChartPalette _p;

    public IndicatorSeriesEngine(ChartPalette? palette = null) => _p = palette ?? ChartPalette.Default;

    public IndicatorRenderData Compute(IReadOnlyList<Bar> bars, IReadOnlySet<string> enabled)
    {
        if (bars.Count == 0 || enabled.Count == 0)
        {
            return IndicatorRenderData.Empty;
        }

        var lines = new List<IndicatorLine>();
        var bands = new List<IndicatorBand>();
        var panes = new List<OscillatorPane>();

        if (enabled.Contains("MA"))
            lines.Add(ScalarLine(bars, "EMA 20", _p.Text, new MovingAverage(20, MovingAverageType.Ema)));

        if (enabled.Contains("BB"))
            bands.Add(BollingerBand(bars));

        if (enabled.Contains("DC"))
            bands.Add(ChannelBand(bars, "Donchian 20", new DonchianChannel(20)));

        if (enabled.Contains("KC"))
            bands.Add(ChannelBand(bars, "Keltner", new KeltnerChannel(20, 10, 2.0)));

        // Panes (oscillators), capped so the reserved strip never crowds the candles.
        foreach (var code in OscillatorCodes)
        {
            if (panes.Count >= 3) break;
            if (!enabled.Contains(code)) continue;
            var pane = BuildPane(code, bars);
            if (pane is not null) panes.Add(pane);
        }

        return new IndicatorRenderData(lines, bands, panes);
    }

    // ── Overlays ────────────────────────────────────────────────────────────

    private IndicatorLine ScalarLine(IReadOnlyList<Bar> bars, string label, RgbaColor color, IScalarIndicator ind)
    {
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            ind.OnValue(bars[i].CloseTicks);
            v[i] = ind.IsReady ? ind.Value : double.NaN;
        }

        return new IndicatorLine(label, color, v);
    }

    private IndicatorBand BollingerBand(IReadOnlyList<Bar> bars)
    {
        var bb = new BollingerBands(20, 2.0);
        var up = new double[bars.Count];
        var mid = new double[bars.Count];
        var lo = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            bb.OnValue(bars[i].CloseTicks);
            up[i] = bb.IsReady ? bb.Upper : double.NaN;
            mid[i] = bb.IsReady ? bb.Middle : double.NaN;
            lo[i] = bb.IsReady ? bb.Lower : double.NaN;
        }

        return new IndicatorBand("Bollinger 20,2", _p.MutedText, up, mid, lo);
    }

    private IndicatorBand ChannelBand(IReadOnlyList<Bar> bars, string label, IBarIndicator ind)
    {
        var up = new double[bars.Count];
        var mid = new double[bars.Count];
        var lo = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            ind.OnBar(bars[i]);
            (double u, double m, double l) = ind switch
            {
                DonchianChannel d => (d.Upper, d.Middle, d.Lower),
                KeltnerChannel k => (k.Upper, k.Middle, k.Lower),
                _ => (double.NaN, double.NaN, double.NaN),
            };
            up[i] = ind.IsReady ? u : double.NaN;
            mid[i] = ind.IsReady ? m : double.NaN;
            lo[i] = ind.IsReady ? l : double.NaN;
        }

        return new IndicatorBand(label, _p.MutedText, up, mid, lo);
    }

    // ── Oscillator panes ────────────────────────────────────────────────────

    private OscillatorPane? BuildPane(string code, IReadOnlyList<Bar> bars) => code switch
    {
        "RSI" => RsiPane(bars),
        "MACD" => MacdPane(bars),
        "ADX" => AdxPane(bars),
        "ATR" => AtrPane(bars),
        "STOCH" => StochPane(bars),
        "CCI" => CciPane(bars),
        "ROC" => RocPane(bars),
        "MOM" => MomentumPane(bars),
        _ => null,
    };

    private OscillatorPane RsiPane(IReadOnlyList<Bar> bars)
    {
        var rsi = new Rsi(14);
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { rsi.OnValue(bars[i].CloseTicks); v[i] = rsi.IsReady ? rsi.Value : double.NaN; }
        return new OscillatorPane("RSI 14", new[] { new IndicatorLine("RSI", _p.Text, v) },
            null, 0, 100, new double[] { 30, 50, 70 }, ZeroCentered: false);
    }

    private OscillatorPane StochPane(IReadOnlyList<Bar> bars)
    {
        var s = new Stochastic();
        var k = new double[bars.Count];
        var d = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { s.OnBar(bars[i]); k[i] = s.IsReady ? s.K : double.NaN; d[i] = s.IsReady ? s.D : double.NaN; }
        return new OscillatorPane("Stochastic 14,3,3",
            new[] { new IndicatorLine("%K", _p.Text, k), new IndicatorLine("%D", _p.MutedText, d) },
            null, 0, 100, new double[] { 20, 80 }, ZeroCentered: false);
    }

    private OscillatorPane AdxPane(IReadOnlyList<Bar> bars)
    {
        var adx = new AverageDirectionalIndex(14);
        var a = new double[bars.Count];
        var plus = new double[bars.Count];
        var minus = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            adx.OnBar(bars[i]);
            a[i] = adx.IsReady ? adx.Value : double.NaN;
            plus[i] = double.IsNaN(adx.PlusDi) ? double.NaN : adx.PlusDi;
            minus[i] = double.IsNaN(adx.MinusDi) ? double.NaN : adx.MinusDi;
        }

        return new OscillatorPane("ADX 14",
            new[]
            {
                new IndicatorLine("ADX", _p.Text, a),
                new IndicatorLine("+DI", _p.BidLiquidity, plus, 1.2f),   // green
                new IndicatorLine("-DI", _p.AskLiquidity, minus, 1.2f),  // light purple
            },
            null, 0, 100, new double[] { 25 }, ZeroCentered: false);
    }

    private OscillatorPane AtrPane(IReadOnlyList<Bar> bars)
    {
        var atr = new AverageTrueRange(14);
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { atr.OnBar(bars[i]); v[i] = atr.IsReady ? atr.Value : double.NaN; }
        return new OscillatorPane("ATR 14 (ticks)", new[] { new IndicatorLine("ATR", _p.Text, v) },
            null, 0, null, Array.Empty<double>(), ZeroCentered: false);
    }

    private OscillatorPane MacdPane(IReadOnlyList<Bar> bars)
    {
        var macd = new Macd();
        var line = new double[bars.Count];
        var sig = new double[bars.Count];
        var hist = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            macd.OnValue(bars[i].CloseTicks);
            line[i] = double.IsNaN(macd.MacdLine) ? double.NaN : macd.MacdLine;
            sig[i] = double.IsNaN(macd.Signal) ? double.NaN : macd.Signal;
            hist[i] = double.IsNaN(macd.Histogram) ? double.NaN : macd.Histogram;
        }

        return new OscillatorPane("MACD 12,26,9",
            new[] { new IndicatorLine("MACD", _p.Text, line), new IndicatorLine("Signal", _p.SelectedObject, sig, 1.2f) },
            hist, null, null, new double[] { 0 }, ZeroCentered: true);
    }

    private OscillatorPane CciPane(IReadOnlyList<Bar> bars)
    {
        var cci = new CommodityChannelIndex(20);
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { cci.OnBar(bars[i]); v[i] = cci.IsReady ? cci.Value : double.NaN; }
        return new OscillatorPane("CCI 20", new[] { new IndicatorLine("CCI", _p.Text, v) },
            null, null, null, new double[] { -100, 0, 100 }, ZeroCentered: true);
    }

    private OscillatorPane RocPane(IReadOnlyList<Bar> bars)
    {
        var roc = new RateOfChange(9);
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { roc.OnValue(bars[i].CloseTicks); v[i] = roc.IsReady ? roc.Value : double.NaN; }
        return new OscillatorPane("ROC 9 (%)", new[] { new IndicatorLine("ROC", _p.Text, v) },
            null, null, null, new double[] { 0 }, ZeroCentered: true);
    }

    private OscillatorPane MomentumPane(IReadOnlyList<Bar> bars)
    {
        var mom = new Momentum(10);
        var v = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++) { mom.OnValue(bars[i].CloseTicks); v[i] = mom.IsReady ? mom.Value : double.NaN; }
        return new OscillatorPane("Momentum 10 (ticks)", new[] { new IndicatorLine("MOM", _p.Text, v) },
            null, null, null, new double[] { 0 }, ZeroCentered: true);
    }
}
