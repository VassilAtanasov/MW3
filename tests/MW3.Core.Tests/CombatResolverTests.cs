namespace MW3.Core.Tests;

/// <summary>
/// <see cref="CombatResolver"/>'s ratio formula (D-29, <c>MW2-RULES.md</c> §4.1): the worked cases
/// settled at FR-3b's kickoff, exercised directly against the resolver rather than through a full
/// <see cref="Match"/> so the arithmetic is pinned independently of the aggregate wiring. Indices are
/// basis points (1/10000, FR-2): identity is <c>10000</c>, not <c>100</c>.
/// </summary>
public class CombatResolverTests
{
    [Fact]
    public void ForgeContribution_IsFixedAtIdentity_UntilG6SuppliesAValue()
    {
        Assert.Equal(100, CombatResolver.ForgeContributionPercent);
    }

    [Fact]
    public void AttackerIndex_IsTenThousand_WithIdentityMoraleAndForge()
    {
        Assert.Equal(10000, CombatResolver.ComposeAttackerIndex(moraleAttackPercent: 100));
    }

    [Theory]
    [InlineData(100, 10000)]
    [InlineData(140, 14000)]
    [InlineData(200, 20000)]
    public void DefenderIndex_EqualsBaseDefencePercentTimesOneHundred_WithIdentityMoraleAndForge(int baseDefencePercent, int expectedIndex)
    {
        Assert.Equal(expectedIndex, CombatResolver.ComposeDefenderIndex(baseDefencePercent, moraleDefencePercent: 100));
    }

    /// <summary>
    /// D-40's multiplicative composition is exact at basis-point scale while forge stays at
    /// identity: a level-2 village (110%) defended at morale 1 (125%) composes to exactly 13750,
    /// not percent scale's floored 137 (settled at FR-2's kickoff).
    /// </summary>
    [Fact]
    public void ComposeDefenderIndex_LevelTwoVillageAtMoraleOne_ComposesExactlyWithNoDivisionLoss()
    {
        var index = CombatResolver.ComposeDefenderIndex(baseDefencePercent: 110, moraleDefencePercent: 125);

        Assert.Equal(13750, index);
    }

    /// <summary>
    /// The attack column and the defence column are not interchangeable (noted at FR-2's kickoff as
    /// the load-bearing correctness risk): at morale 3 an attacker composes 11500 and a defender
    /// composes 17500 - this fails if the two are exchanged.
    /// </summary>
    [Fact]
    public void AttackAndDefenceColumns_AtMoraleThree_AreNotInterchangeable()
    {
        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(3));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(baseDefencePercent: 100, MoraleTable.DefencePercentage(3));

        Assert.Equal(11500, attackerIndex);
        Assert.Equal(17500, defenderIndex);
        Assert.NotEqual(attackerIndex, defenderIndex);
    }

    /// <summary>100% is unchanged from today: bit-identical to phase 2's plain 1:1 arithmetic.</summary>
    [Fact]
    public void AtEqualIndices_TenVersusTen_DefenderHoldsAtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 10000, waveUnits: 10, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    [Fact]
    public void AtEqualIndices_ElevenVersusTen_CapturesWithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 10000, waveUnits: 11, defendingGarrison: 10);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    /// <summary>A level-1 tower (140%) holding 10 survives a 14-unit wave at exactly zero.</summary>
    [Fact]
    public void LevelOneTower_Holding10_Survives14UnitWave_AtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 14000, waveUnits: 14, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    /// <summary>The same tower falls to a 15-unit wave, arriving with 1.</summary>
    [Fact]
    public void LevelOneTower_Holding10_FallsTo15UnitWave_WithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 14000, waveUnits: 15, defendingGarrison: 10);

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
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 20000, waveUnits: 1, defendingGarrison: 0);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    /// <summary>A level-3 village (120%) holding 10 survives a 12-unit wave at exactly zero.</summary>
    [Fact]
    public void LevelThreeVillage_Holding10_Survives12UnitWave_AtZero()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 12000, waveUnits: 12, defendingGarrison: 10);

        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }

    /// <summary>The same village falls to a 13-unit wave, arriving with 1.</summary>
    [Fact]
    public void LevelThreeVillage_Holding10_FallsTo13UnitWave_WithOne()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 12000, waveUnits: 13, defendingGarrison: 10);

        Assert.True(result.Captured);
        Assert.Equal(1, result.RemainingGarrison);
    }

    [Fact]
    public void ExactTie_LeavesTheDefenderHoldingZero_NeverCaptures()
    {
        var result = CombatResolver.Resolve(attackerIndex: 10000, defenderIndex: 14000, waveUnits: 7, defendingGarrison: 5);

        // 7 * 10000 = 70000 == 5 * 14000 = 70000: strictly greater is required to capture.
        Assert.False(result.Captured);
        Assert.Equal(0, result.RemainingGarrison);
    }
}
