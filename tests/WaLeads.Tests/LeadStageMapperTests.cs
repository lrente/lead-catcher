using WaLeads.Core;

namespace WaLeads.Tests;

public sealed class LeadStageMapperTests
{
    [Fact]
    public void SpamMapsToLost()
    {
        Assert.Equal("lost", LeadStageMapper.Map("qualifying", "spam", 100, 70));
    }

    [Fact]
    public void ScoreAtThresholdMapsToHot()
    {
        Assert.Equal("hot", LeadStageMapper.Map("new", "pricing", 70, 70));
    }

    [Fact]
    public void LowerScoreMapsToQualifying()
    {
        Assert.Equal("qualifying", LeadStageMapper.Map("new", "info", 69, 70));
    }

    [Fact]
    public void BookedStageIsNeverDowngraded()
    {
        Assert.Equal("booked", LeadStageMapper.Map("booked", "spam", 0, 70));
    }
}
