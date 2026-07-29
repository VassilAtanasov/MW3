namespace MW3.Core.Tests;

/// <summary>
/// <see cref="CombatResolver"/>'s ratio formula (D-29, <c>MW2-RULES.md</c> §4.1): the worked cases
/// settled at FR-3b's kickoff, exercised directly against the resolver rather than through a full
/// <see cref="Match"/> so the arithmetic is pinned independently of the aggregate wiring.
/// </summary>
public class CombatResolverTests
{
    [Fact]
    public void MoraleAndForgeContributions_AreFixedAtIdentity_UntilG1AndG6SupplyAValue()
    {
        Assert.Equal(100, CombatResolver.MoraleContributionPercent);
        Assert.Equal(100, CombatResolver.ForgeContributionPercent);
    }

    [Fact]
    public void AttackerIndex_IsOneHundred_WithIdentityMoraleAndForge()
    {
        Assert.Equal(100, CombatResolver.ComposeAttackerIndex());
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(140, 140)]
    [InlineData(200, 200)]
    public void DefenderIndex_EqualsBaseDefencePercent_WithIdentityMoraleAndForge(int baseDefencePercent, int expectedIndex)
    {
        Assert.Equal(expectedIndex, CombatResolver.ComposeDefenderIndex(baseDefencePercent));
    }

    /// <summary>100% is unchanged from today: bit-identical to phase 2's plain 1:1 arithmetic.</summary>
    [Fact]
    public void AtEqualIndices_TenVersusTen_DefenderHoldsAtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 100, waveUnits: 10, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    [Fact]
    public void AtEqualIndices_ElevenVersusTen_CapturesWithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 100, waveUnits: 11, defendingGarrison: 10);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    /// <summary>A level-1 tower (140%) holding 10 survives a 14-unit wave at exactly zero.</summary>
    [Fact]
    public void LevelOneTower_Holding10_Survives14UnitWave_AtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 140, waveUnits: 14, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    /// <summary>The same tower falls to a 15-unit wave, arriving with 1.</summary>
    [Fact]
    public void LevelOneTower_Holding10_FallsTo15UnitWave_WithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 140, waveUnits: 15, defendingGarrison: 10);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    /// <summary>
    /// An emptied level-4 tower (200%) is still takeable by one unit: Du*d is zero so no rounding
    /// rule is needed to make this work, preserving the rule FR-1 and FR-3 shipped.
    /// </summary>
    [Fact]
    public void EmptyLevelFourTower_IsCapturedByOneUnit_ArrivingWithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 200, waveUnits: 1, defendingGarrison: 0);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    /// <summary>A level-3 village (120%) holding 10 survives a 12-unit wave at exactly zero.</summary>
    [Fact]
    public void LevelThreeVillage_Holding10_Survives12UnitWave_AtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 120, waveUnits: 12, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    /// <summary>The same village falls to a 13-unit wave, arriving with 1.</summary>
    [Fact]
    public void LevelThreeVillage_Holding10_FallsTo13UnitWave_WithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 120, waveUnits: 13, defendingGarrison: 10);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    [Fact]
    public void ExactTie_LeavesTheDefenderHoldingZero_NeverCaptures()
    {
        var result = CombatResolver.Resolve(attackerIndex: 100, defenderIndex: 140, waveUnits: 7, defendingGarrison: 5);

        // 7 * 100 = 700 == 5 * 140 = 700: strictly greater is required to capture.
        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }
}
