namespace MW3.Core.Tests;

public class LevelTableTests
{
    [Fact]
    public void Village_Ladder_IsFiveLevels()
    {
        Assert.Equal(1, LevelTable.MinLevel);
        Assert.Equal(5, LevelTable.Village.MaxLevel);
        Assert.Equal(5, LevelTable.MaxLevel(BaseType.Producer));
    }

    [Fact]
    public void Tower_Ladder_IsFourLevels()
    {
        Assert.Equal(4, LevelTable.Tower.MaxLevel);
        Assert.Equal(4, LevelTable.MaxLevel(BaseType.Tower));
    }

    /// <summary>
    /// Level 5 exists in the village ladder - its cap and period can be looked up - but is not
    /// reachable through an <see cref="UpgradeCommand"/>: MW2 publishes no price for it. A tower's
    /// upgradable ceiling equals its ladder ceiling; it has no unreachable top tier.
    /// </summary>
    [Fact]
    public void MaxUpgradableLevel_IsLowerThanMaxLevel_OnlyForTheVillage()
    {
        Assert.Equal(4, LevelTable.Village.MaxUpgradableLevel);
        Assert.Equal(4, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        Assert.True(LevelTable.MaxUpgradableLevel(BaseType.Producer) < LevelTable.MaxLevel(BaseType.Producer));

        Assert.Equal(4, LevelTable.Tower.MaxUpgradableLevel);
        Assert.Equal(4, LevelTable.MaxUpgradableLevel(BaseType.Tower));
        Assert.Equal(LevelTable.MaxLevel(BaseType.Tower), LevelTable.MaxUpgradableLevel(BaseType.Tower));
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 60)]
    [InlineData(4, 80)]
    [InlineData(5, 100)]
    public void Village_GarrisonCap_MatchesTheAgreedLadder(int level, int expectedCap)
    {
        Assert.Equal(expectedCap, LevelTable.Village.GarrisonCap(level));
        Assert.Equal(expectedCap, LevelTable.GarrisonCap(BaseType.Producer, level));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 30)]
    [InlineData(3, 20)]
    [InlineData(4, 15)]
    [InlineData(5, 12)]
    public void Village_ProductionPeriodTicks_MatchesTheAgreedLadder(int level, long expectedPeriod)
    {
        Assert.Equal(expectedPeriod, LevelTable.Village.ProductionPeriodTicks(level));
    }

    /// <summary>Production is asserted in wall-clock too, not just ticks (MW2-RULES.md §2.2, §3).</summary>
    [Theory]
    [InlineData(1, 3000)]
    [InlineData(2, 1500)]
    [InlineData(3, 1000)]
    [InlineData(4, 750)]
    [InlineData(5, 600)]
    public void Village_ProductionPeriod_MatchesTheAgreedWallClock(int level, long expectedMilliseconds)
    {
        Assert.Equal(expectedMilliseconds, LevelTable.Village.ProductionPeriodTicks(level) * Match.TickDurationMilliseconds);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    public void Village_UpgradeCost_MatchesTheAgreedLadder(int fromLevel, int expectedCost)
    {
        Assert.Equal(expectedCost, LevelTable.Village.UpgradeCost(fromLevel));
        Assert.Equal(expectedCost, LevelTable.UpgradeCost(BaseType.Producer, fromLevel));
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 20)]
    [InlineData(3, 20)]
    public void Tower_UpgradeCost_IsFlatTwenty(int fromLevel, int expectedCost)
    {
        Assert.Equal(expectedCost, LevelTable.Tower.UpgradeCost(fromLevel));
        Assert.Equal(expectedCost, LevelTable.UpgradeCost(BaseType.Tower, fromLevel));
    }

    /// <summary>Towers have no garrison cap at any level (MW2-RULES.md §2.3): every level answers null.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Tower_GarrisonCap_IsAlwaysNull(int level)
    {
        Assert.Null(LevelTable.GarrisonCap(BaseType.Tower, level));
    }

    [Fact]
    public void FirstUpgrade_IsAffordableFromTheStartingGarrisonWithoutWaiting()
    {
        var match = new Match();
        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);

        Assert.True(LevelTable.Village.UpgradeCost(LevelTable.MinLevel) <= humanBase.GarrisonCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Village_GarrisonCap_OutsideTheLadder_Throws(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.Village.GarrisonCap(level));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Tower_UpgradeCost_OutsideTheUpgradableRange_Throws(int fromLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.Tower.UpgradeCost(fromLevel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Village_UpgradeCost_OutsideTheUpgradableRange_Throws(int fromLevel)
    {
        // The cost of upgrading *from* the maximum upgradable level is not a number - a caller must
        // reject an already-maxed base rather than ask what the impossible upgrade would cost.
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.Village.UpgradeCost(fromLevel));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Village_RingThicknessFractionOfRadius_IsDefinedForEveryLevel_AndPositive(int level)
    {
        Assert.True(LevelTable.Village.RingThicknessFractionOfRadius(level) > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Tower_RingThicknessFractionOfRadius_IsDefinedForEveryLevel_AndPositive(int level)
    {
        Assert.True(LevelTable.Tower.RingThicknessFractionOfRadius(level) > 0);
    }

    [Fact]
    public void Village_RingThicknessFractionOfRadius_StrictlyIncreasesWithLevel()
    {
        for (var level = LevelTable.MinLevel; level < LevelTable.Village.MaxLevel; level++)
        {
            Assert.True(
                LevelTable.Village.RingThicknessFractionOfRadius(level + 1) > LevelTable.Village.RingThicknessFractionOfRadius(level));
        }
    }

    [Fact]
    public void Tower_RingThicknessFractionOfRadius_StrictlyIncreasesWithLevel()
    {
        for (var level = LevelTable.MinLevel; level < LevelTable.Tower.MaxLevel; level++)
        {
            Assert.True(
                LevelTable.Tower.RingThicknessFractionOfRadius(level + 1) > LevelTable.Tower.RingThicknessFractionOfRadius(level));
        }
    }

    [Fact]
    public void Village_HigherLevels_AreStrictlyBetterEconomy_NeverWorse()
    {
        for (var level = LevelTable.MinLevel; level < LevelTable.Village.MaxLevel; level++)
        {
            Assert.True(LevelTable.Village.GarrisonCap(level + 1) > LevelTable.Village.GarrisonCap(level));
            Assert.True(LevelTable.Village.ProductionPeriodTicks(level + 1) < LevelTable.Village.ProductionPeriodTicks(level));
        }
    }

    /// <summary>A level-1 village's cap (20) is below the conversion cost (30): it cannot convert.</summary>
    [Fact]
    public void LevelOneVillage_CannotAffordConversion()
    {
        Assert.True(LevelTable.Village.GarrisonCap(LevelTable.MinLevel) < LevelTable.ConversionCost);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 110)]
    [InlineData(3, 120)]
    [InlineData(4, 130)]
    [InlineData(5, 140)]
    public void Village_DefencePercentage_MatchesTheAgreedLadder(int level, int expectedPercent)
    {
        Assert.Equal(expectedPercent, LevelTable.Village.DefencePercentage(level));
        Assert.Equal(expectedPercent, LevelTable.DefencePercentage(BaseType.Producer, level));
    }

    [Theory]
    [InlineData(1, 140)]
    [InlineData(2, 170)]
    [InlineData(3, 190)]
    [InlineData(4, 200)]
    public void Tower_DefencePercentage_MatchesTheAgreedLadder(int level, int expectedPercent)
    {
        Assert.Equal(expectedPercent, LevelTable.Tower.DefencePercentage(level));
        Assert.Equal(expectedPercent, LevelTable.DefencePercentage(BaseType.Tower, level));
    }

    /// <summary>
    /// The reference's own stated consequence (D-29): a level-1 tower defends exactly as well as a
    /// fully upgraded village, which is what makes a tower a defensive structure rather than one that
    /// merely trades production for range.
    /// </summary>
    [Fact]
    public void LevelOneTower_AndLevelFiveVillage_DefendIdentically()
    {
        Assert.Equal(
            LevelTable.Village.DefencePercentage(LevelTable.Village.MaxLevel),
            LevelTable.Tower.DefencePercentage(LevelTable.MinLevel));
    }

    /// <summary>A neutral base is a level-1 producer, so taking neutrals is unchanged by D-29.</summary>
    [Fact]
    public void NeutralBase_DefendsAtOneHundredPercent()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Equal(100, neutral.DefencePercentage);
    }
}
