using FlowTerminal.Analytics.Footprints;
using FlowTerminal.Domain.Events;
using FlowTerminal.Domain.Instruments;
using Xunit;

namespace FlowTerminal.Analytics.Tests;

/// <summary>
/// Tests the footprint customization layer: data-mode/visual-layout compatibility,
/// the built-in preset registry, settings validation, and the guarantee that visual
/// settings never change the underlying calculated bar.
/// </summary>
public class FootprintCustomizationTests
{
    private static Footprint Fixture()
    {
        var f = new Footprint();
        f.AddAt(100, 9, AggressorSide.Buy); f.AddAt(100, 1, AggressorSide.Sell);
        f.AddAt(101, 2, AggressorSide.Buy); f.AddAt(101, 10, AggressorSide.Sell);
        f.AddAt(102, 8, AggressorSide.Buy); f.AddAt(102, 3, AggressorSide.Sell);
        return f;
    }

    [Theory]
    [InlineData(FootprintMode.BidAsk)]
    [InlineData(FootprintMode.Delta)]
    [InlineData(FootprintMode.TotalVolume)]
    [InlineData(FootprintMode.VolumeProfile)]
    [InlineData(FootprintMode.DeltaPercent)]
    [InlineData(FootprintMode.TradeCount)]
    public void Default_Layout_Is_Compatible_With_Its_Mode(FootprintMode mode)
    {
        var def = FootprintCompatibility.DefaultLayout(mode);
        Assert.True(FootprintCompatibility.IsCompatible(mode, def), $"{mode} default {def} not compatible");
    }

    [Fact]
    public void Resolve_Snaps_Incompatible_Layout_To_Default()
    {
        // MirroredHistogram is not a Bid×Ask layout → snaps to the Bid×Ask default (SplitText).
        Assert.False(FootprintCompatibility.IsCompatible(FootprintMode.BidAsk, FootprintVisualLayout.MirroredHistogram));
        Assert.Equal(FootprintVisualLayout.SplitText,
            FootprintCompatibility.Resolve(FootprintMode.BidAsk, FootprintVisualLayout.MirroredHistogram));

        // A compatible pair is preserved.
        Assert.Equal(FootprintVisualLayout.Histogram,
            FootprintCompatibility.Resolve(FootprintMode.BidAsk, FootprintVisualLayout.Histogram));
    }

    [Fact]
    public void Validate_Resolves_Layout_And_Clamps_Opacity()
    {
        var s = (FootprintSettings.Default with
        {
            Mode = FootprintMode.BidAsk,
            VisualLayout = FootprintVisualLayout.MirroredHistogram, // incompatible
            CellOpacity = 5.0,
            TextOpacity = -1.0,
        }).Validate();

        Assert.Equal(FootprintVisualLayout.SplitText, s.VisualLayout);
        Assert.InRange(s.CellOpacity, 0.05, 1.0);
        Assert.InRange(s.TextOpacity, 0.1, 1.0);
    }

    [Fact]
    public void Preset_Registry_Has_Twelve_Protected_Built_Ins()
    {
        Assert.Equal(12, FootprintPresetRegistry.BuiltIns.Count);
        Assert.All(FootprintPresetRegistry.BuiltIns, p =>
        {
            Assert.True(p.IsBuiltIn);
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            // every preset's stored layout is compatible with its mode after validation
            var v = p.Settings.Validate();
            Assert.True(FootprintCompatibility.IsCompatible(v.Mode, v.VisualLayout));
        });
        Assert.NotNull(FootprintPresetRegistry.ByName("Delta Profile"));
        Assert.Equal("Classic Bid×Ask", FootprintPresetRegistry.Default.Name);
    }

    [Fact]
    public void Preset_ForInstrument_Uses_Instrument_Thresholds()
    {
        var preset = FootprintPresetRegistry.ByName("Classic Bid×Ask")!;
        var nq = preset.ForInstrument(RootSymbol.NQ);
        var es = preset.ForInstrument(RootSymbol.ES);
        Assert.Equal(FootprintSettings.Nq.LargeTradeThreshold, nq.LargeTradeThreshold);
        Assert.Equal(FootprintSettings.Es.LargeTradeThreshold, es.LargeTradeThreshold);
        Assert.True(es.LargeTradeThreshold > nq.LargeTradeThreshold);
    }

    [Fact]
    public void Visual_Settings_Never_Change_The_Calculated_Bar()
    {
        FootprintBar Build(FootprintSettings s) =>
            FootprintAggregator.Build(Fixture(), 100, 102, 100, 102, default, default, true, s);

        var a = Build(FootprintSettings.Default);
        var b = Build(FootprintSettings.Default with
        {
            VisualLayout = FootprintVisualLayout.GradientCell,
            Background = FootprintBackground.Delta,
            CellOpacity = 0.5,
            ShowCandleBody = false,
            Separator = CellSeparator.Pipe,
        });

        // Pure visual changes → identical data and identical replay hash.
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(a.PocTicks, b.PocTicks);
        Assert.Equal(a.Delta, b.Delta);
    }
}
