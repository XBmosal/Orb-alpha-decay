using FlowTerminal.Charting.Studies;
using Xunit;

namespace FlowTerminal.UiTests;

public class StudyCatalogTests
{
    [Fact]
    public void Catalog_Contains_All_Requested_Studies()
    {
        var codes = StudyCatalog.All.Select(s => s.ShortCode).ToHashSet();
        foreach (var expected in new[] { "VBP", "BAC", "DP", "TPO", "VPS", "VWAP", "FP", "IMB", "CVD", "LT", "ICE", "SOT", "SR", "FVG", "ORB", "ADR", "MTF", "AD", "GAP" })
        {
            Assert.Contains(expected, codes);
        }
    }

    [Fact]
    public void Every_Category_Has_Entries()
    {
        foreach (StudyCategory c in Enum.GetValues<StudyCategory>())
        {
            Assert.NotEmpty(StudyCatalog.ByCategory(c));
        }
    }

    [Fact]
    public void Statuses_Are_Honest_Not_All_Active()
    {
        // The catalog must reflect real status — some Active, some EngineReady, some Planned.
        Assert.Contains(StudyCatalog.All, s => s.Status == StudyStatus.Active);
        Assert.Contains(StudyCatalog.All, s => s.Status == StudyStatus.EngineReady);
        Assert.Contains(StudyCatalog.All, s => s.Status == StudyStatus.Planned);
    }

    [Fact]
    public void Detector_Backed_Studies_Reference_A_Detector_Key()
    {
        var large = StudyCatalog.All.Single(s => s.ShortCode == "LT");
        Assert.Equal("Large Trade", large.DetectorKey);
    }
}
