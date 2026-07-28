namespace MW3.Core.Tests;

public class LevelTableTests
{
    [Fact]
    public void Ladder_IsThreeLevels()
    {
        Assert.Equal(1, LevelTable.MinLevel);
        Assert.Equal(3, LevelTable.MaxLevel);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 35)]
    [InlineData(3, 50)]
    public void GarrisonCap_MatchesTheAgreedLadder(int level, int expectedCap)
    {
        Assert.Equal(expectedCap, LevelTable.GarrisonCap(level));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 7)]
    [InlineData(3, 5)]
    public void ProductionPeriodTicks_MatchesTheAgreedLadder(int level, long expectedPeriod)
    {
        Assert.Equal(expectedPeriod, LevelTable.ProductionPeriodTicks(level));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 16)]
    public void UpgradeCost_MatchesTheAgreedLadder(int fromLevel, int expectedCost)
    {
        Assert.Equal(expectedCost, LevelTable.UpgradeCost(fromLevel));
    }

    [Fact]
    public void FirstUpgrade_IsAffordableFromTheStartingGarrisonWithoutWaiting()
    {
        var match = new Match();
        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);

        Assert.True(LevelTable.UpgradeCost(LevelTable.MinLevel) <= humanBase.GarrisonCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void GarrisonCap_OutsideTheLadder_Throws(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.GarrisonCap(level));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void UpgradeCost_OutsideTheUpgradableRange_Throws(int fromLevel)
    {
        // The cost of upgrading *from* the maximum level is not a number - a caller must reject an
        // already-maxed base rather than ask what the impossible upgrade would cost.
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.UpgradeCost(fromLevel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RingThicknessFractionOfRadius_IsDefinedForEveryLevel_AndPositive(int level)
    {
        Assert.True(LevelTable.RingThicknessFractionOfRadius(level) > 0);
    }

    [Fact]
    public void RingThicknessFractionOfRadius_StrictlyIncreasesWithLevel()
    {
        for (var level = LevelTable.MinLevel; level < LevelTable.MaxLevel; level++)
        {
            Assert.True(LevelTable.RingThicknessFractionOfRadius(level + 1) > LevelTable.RingThicknessFractionOfRadius(level));
        }
    }

    [Fact]
    public void HigherLevels_AreStrictlyBetterEconomy_NeverWorse()
    {
        for (var level = LevelTable.MinLevel; level < LevelTable.MaxLevel; level++)
        {
            Assert.True(LevelTable.GarrisonCap(level + 1) > LevelTable.GarrisonCap(level));
            Assert.True(LevelTable.ProductionPeriodTicks(level + 1) < LevelTable.ProductionPeriodTicks(level));
        }
    }
}
