namespace MW3.Core.Tests;

public class MoraleTableTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(499, 0)]
    [InlineData(500, 1)]
    [InlineData(999, 1)]
    [InlineData(1000, 2)]
    [InlineData(1999, 2)]
    [InlineData(2000, 3)]
    [InlineData(3999, 3)]
    [InlineData(4000, 4)]
    [InlineData(7999, 4)]
    [InlineData(8000, 5)]
    public void LevelForPoints_IsTheHighestThresholdReached(int points, int expectedLevel)
    {
        Assert.Equal(expectedLevel, MoraleTable.LevelForPoints(points));
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(1, 125, 105, 110)]
    [InlineData(2, 150, 110, 120)]
    [InlineData(3, 175, 115, 130)]
    [InlineData(4, 200, 120, 140)]
    [InlineData(5, 225, 125, 150)]
    public void Ladder_MatchesTheAgreedPercentages(int level, int defence, int attack, int unitSpeed)
    {
        Assert.Equal(defence, MoraleTable.DefencePercentage(level));
        Assert.Equal(attack, MoraleTable.AttackPercentage(level));
        Assert.Equal(unitSpeed, MoraleTable.UnitSpeedPercentage(level));
    }

    [Fact]
    public void ClampPoints_ClampsToFloorAndCeiling()
    {
        Assert.Equal(MoraleTable.PointFloor, MoraleTable.ClampPoints(-500));
        Assert.Equal(MoraleTable.PointFloor, MoraleTable.ClampPoints(0));
        Assert.Equal(MoraleTable.PointCeiling, MoraleTable.ClampPoints(MoraleTable.PointCeiling));
        Assert.Equal(MoraleTable.PointCeiling, MoraleTable.ClampPoints(MoraleTable.PointCeiling + 1000));
        Assert.Equal(4000, MoraleTable.ClampPoints(4000));
    }

    [Theory]
    [InlineData(1, false, 40)]
    [InlineData(2, false, 100)]
    [InlineData(3, false, 160)]
    [InlineData(4, false, 220)]
    [InlineData(5, false, 300)]
    [InlineData(1, true, 100)]
    [InlineData(2, true, 250)]
    [InlineData(3, true, 400)]
    [InlineData(4, true, 550)]
    [InlineData(5, true, 750)]
    public void Village_CaptureGain_MatchesTheAgreedTable(int level, bool wasOpponentOwned, int expectedGain)
    {
        Assert.Equal(expectedGain, MoraleTable.Village.CaptureGain(level, wasOpponentOwned));
        Assert.Equal(expectedGain, MoraleTable.CaptureGain(BaseType.Producer, level, wasOpponentOwned));
    }

    [Theory]
    [InlineData(1, false, 80)]
    [InlineData(2, false, 200)]
    [InlineData(3, false, 320)]
    [InlineData(4, false, 440)]
    [InlineData(1, true, 200)]
    [InlineData(2, true, 500)]
    [InlineData(3, true, 800)]
    [InlineData(4, true, 1100)]
    public void Tower_CaptureGain_MatchesTheAgreedTable(int level, bool wasOpponentOwned, int expectedGain)
    {
        Assert.Equal(expectedGain, MoraleTable.Tower.CaptureGain(level, wasOpponentOwned));
        Assert.Equal(expectedGain, MoraleTable.CaptureGain(BaseType.Tower, level, wasOpponentOwned));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 120)]
    [InlineData(3, 200)]
    [InlineData(4, 280)]
    [InlineData(5, 380)]
    public void Village_CaptureLoss_MatchesTheAgreedTable(int level, int expectedLoss)
    {
        Assert.Equal(expectedLoss, MoraleTable.Village.CaptureLoss(level));
        Assert.Equal(expectedLoss, MoraleTable.CaptureLoss(BaseType.Producer, level));
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 250)]
    [InlineData(3, 400)]
    [InlineData(4, 550)]
    public void Tower_CaptureLoss_MatchesTheAgreedTable(int level, int expectedLoss)
    {
        Assert.Equal(expectedLoss, MoraleTable.Tower.CaptureLoss(level));
        Assert.Equal(expectedLoss, MoraleTable.CaptureLoss(BaseType.Tower, level));
    }

    [Theory]
    [InlineData(1, 50)] // unreachable in play, but recorded rather than hidden
    [InlineData(2, 100)]
    [InlineData(3, 150)]
    [InlineData(4, 200)]
    public void Village_UpgradeGain_MatchesTheAgreedTable(int toLevel, int expectedGain)
    {
        Assert.Equal(expectedGain, MoraleTable.Village.UpgradeGain(toLevel));
        Assert.Equal(expectedGain, MoraleTable.UpgradeGain(BaseType.Producer, toLevel));
    }

    [Theory]
    [InlineData(1, 100)] // unreachable in play, but recorded rather than hidden
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    [InlineData(4, 400)]
    public void Tower_UpgradeGain_MatchesTheAgreedTable(int toLevel, int expectedGain)
    {
        Assert.Equal(expectedGain, MoraleTable.Tower.UpgradeGain(toLevel));
        Assert.Equal(expectedGain, MoraleTable.UpgradeGain(BaseType.Tower, toLevel));
    }

    [Fact]
    public void AttackingUnitGainAndLoss_AreBothTenPoints()
    {
        Assert.Equal(10, MoraleTable.AttackingUnitDestroyedGain);
        Assert.Equal(10, MoraleTable.AttackingUnitDiedLoss);
    }

    [Fact]
    public void OutOfRangeLevel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.DefencePercentage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.DefencePercentage(6));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Village.CaptureGain(0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Village.CaptureGain(6, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Tower.CaptureGain(0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Tower.CaptureGain(5, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Village.UpgradeGain(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoraleTable.Village.UpgradeGain(5));
    }
}
