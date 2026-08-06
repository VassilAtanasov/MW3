namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-3: <see cref="ForgeTable"/> is the published count-to-percentage ladder
/// (<c>MW2-RULES.md</c> §2.4, <c>docs/forges/REQUIREMENTS.md</c> §4 "Tuning values") and the only
/// place a forge percentage literal appears (D-22). See issue #87's acceptance criteria.
/// </summary>
public class ForgeTableTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 125)]
    [InlineData(2, 135)]
    [InlineData(3, 145)]
    [InlineData(4, 150)]
    public void DefencePercentage_MatchesThePublishedLadder(int forgeCount, int expected)
    {
        Assert.Equal(expected, ForgeTable.DefencePercentage(forgeCount));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 150)]
    [InlineData(2, 175)]
    [InlineData(3, 190)]
    [InlineData(4, 200)]
    public void AttackPercentage_MatchesThePublishedLadder(int forgeCount, int expected)
    {
        Assert.Equal(expected, ForgeTable.AttackPercentage(forgeCount));
    }

    /// <summary>
    /// The attack column and the defence column are not interchangeable - a forge buys far more
    /// attack than defence, which is what makes it an aggressive investment rather than a second
    /// kind of tower.
    /// </summary>
    [Fact]
    public void AttackAndDefenceColumns_AreNotInterchangeable()
    {
        for (var n = 1; n <= ForgeTable.MaxContributingForges; n++)
        {
            Assert.True(ForgeTable.AttackPercentage(n) > ForgeTable.DefencePercentage(n));
        }
    }

    /// <summary>
    /// Holding more than <see cref="ForgeTable.MaxContributingForges"/> is legal play that simply
    /// buys nothing: both columns clamp to the four-forge row rather than throwing or running off
    /// the end of the ladder.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void BeyondTheCap_ReturnsTheCapRow_AndNeverThrows(int forgeCount)
    {
        Assert.Equal(ForgeTable.DefencePercentage(ForgeTable.MaxContributingForges), ForgeTable.DefencePercentage(forgeCount));
        Assert.Equal(ForgeTable.AttackPercentage(ForgeTable.MaxContributingForges), ForgeTable.AttackPercentage(forgeCount));
    }

    /// <summary>A negative count is a caller bug, not a legal holding, and is rejected rather than clamped.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ANegativeCount_Throws(int forgeCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ForgeTable.DefencePercentage(forgeCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => ForgeTable.AttackPercentage(forgeCount));
    }

    /// <summary>Zero forges is identity in both columns - the term a player who holds none composes with.</summary>
    [Fact]
    public void MinForgeCount_IsIdentityInBothColumns()
    {
        Assert.Equal(100, ForgeTable.AttackPercentage(ForgeTable.MinForgeCount));
        Assert.Equal(100, ForgeTable.DefencePercentage(ForgeTable.MinForgeCount));
    }

    /// <summary>
    /// The cap is named, not a literal 4 scattered across call sites - this reads the constant to
    /// pin its value, so moving the ladder moves one number in one place.
    /// </summary>
    [Fact]
    public void TheCap_IsFourForges()
    {
        Assert.Equal(4, ForgeTable.MaxContributingForges);
    }
}
