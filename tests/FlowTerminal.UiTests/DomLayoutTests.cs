using FlowTerminal.Charting.Dom;
using Xunit;

namespace FlowTerminal.UiTests;

/// <summary>Unit tests for the editable DOM column layout: build-from-preset, show/hide,
/// reorder, resize (with clamping) and serialise/deserialise round-trips.</summary>
public class DomLayoutTests
{
    [Fact]
    public void FromPreset_ShowsPresetColumnsFirstThenHidesTheRest()
    {
        var preset = DomPresetRegistry.ByName("Classic Depth")!;
        var layout = DomLayout.FromPreset(preset);

        // Every known column is present exactly once.
        Assert.Equal(DomColumnRegistry.All.Count, layout.Columns.Count);
        Assert.Equal(DomColumnRegistry.All.Count, layout.Columns.Select(c => c.Type).Distinct().Count());

        // The visible, ordered set equals the preset's columns.
        Assert.Equal(preset.Columns, layout.ResolveColumns());
        Assert.Equal(preset.Columns.Count, layout.VisibleCount);
    }

    [Fact]
    public void Hiding_A_Column_Removes_It_From_The_Resolved_Set()
    {
        var layout = DomLayout.FromPreset(DomPresetRegistry.ByName("Classic Depth")!);
        layout.SetVisible(DomColumnType.BidCumulative, false);

        Assert.DoesNotContain(DomColumnType.BidCumulative, layout.ResolveColumns());
        Assert.Equal(layout.ResolveColumns().Count, layout.ResolveWidths().Count);
    }

    [Fact]
    public void MoveUp_And_MoveDown_Reorder_Visible_Columns()
    {
        var layout = DomLayout.FromPreset(DomPresetRegistry.ByName("Classic Depth")!);
        var before = layout.ResolveColumns().ToList();

        Assert.True(layout.MoveDown(0));
        var after = layout.ResolveColumns().ToList();
        Assert.Equal(before[1], after[0]);
        Assert.Equal(before[0], after[1]);

        // Edges return false and leave order unchanged.
        Assert.False(layout.MoveUp(0));
        Assert.False(layout.MoveDown(layout.Columns.Count - 1));
    }

    [Fact]
    public void SetWidth_Clamps_To_Bounds()
    {
        var layout = DomLayout.FromPreset(DomPresetRegistry.Default);
        layout.SetWidth(DomColumnType.Price, 5000);
        layout.SetWidth(DomColumnType.BidSize, 1);

        double price = layout.Columns.First(c => c.Type == DomColumnType.Price).Width;
        double bid = layout.Columns.First(c => c.Type == DomColumnType.BidSize).Width;
        Assert.Equal(DomLayout.MaxWidth, price);
        Assert.Equal(DomLayout.MinWidth, bid);
    }

    [Fact]
    public void Serialize_Then_Deserialize_Round_Trips()
    {
        var layout = DomLayout.FromPreset(DomPresetRegistry.ByName("Order Flow")!);
        layout.SetVisible(DomColumnType.MaxTrade, true);
        layout.SetWidth(DomColumnType.Price, 96);
        layout.MoveDown(0);

        var restored = DomLayout.Deserialize(layout.Serialize());
        Assert.NotNull(restored);
        Assert.Equal(layout.ResolveColumns(), restored!.ResolveColumns());
        Assert.Equal(
            layout.Columns.First(c => c.Type == DomColumnType.Price).Width,
            restored.Columns.First(c => c.Type == DomColumnType.Price).Width);
    }

    [Fact]
    public void Deserialize_Is_Robust_To_Garbage_And_Backfills_New_Columns()
    {
        // Only one valid token; unknown ids skipped; all other known columns appended hidden.
        var restored = DomLayout.Deserialize("price:1:80,bogus:1:50,,nonsense");
        Assert.NotNull(restored);
        Assert.Equal(DomColumnRegistry.All.Count, restored!.Columns.Count);
        Assert.Equal(new[] { DomColumnType.Price }, restored.ResolveColumns());

        Assert.Null(DomLayout.Deserialize(""));
        Assert.Null(DomLayout.Deserialize(null));
        Assert.Null(DomLayout.Deserialize("only:garbage:here"));
    }
}
