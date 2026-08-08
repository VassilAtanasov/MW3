namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-4: the forge's place in phase 5's morale system - capturing one gains points, losing
/// one costs them, and neither conversion nor a levelless recapture disturbs the rule. Exercised
/// against an injected layout containing a neutral forge (FR-1, D-44), since the shipped map gains
/// one only at FR-2. See issue #83's acceptance criteria.
/// </summary>
public class ForgeMoraleTests
{
    private static readonly MapSlot[] _layoutWithNeutralForge =
    {
        new(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.50, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Forge, LevelTable.MinLevel),
    };

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetLastOwnerChangeTick(Base b, long? tick) =>
        typeof(Base).GetProperty(nameof(Base.LastOwnerChangeTick))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { tick });

    private static void SetOwnerBeforeLastChange(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.OwnerBeforeLastChange))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    /// <summary>
    /// Capturing a neutral forge awards the capturer 200 and charges nobody - a neutral base has no
    /// previous owner, the same rule every other neutral capture already follows.
    /// </summary>
    [Fact]
    public void CapturingANeutralForge_Awards200_ChargesNobody()
    {
        var match = new Match(_layoutWithNeutralForge);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var forge = match.Bases.Single(b => b.Type == BaseType.Forge);
        SetGarrison(human, 40);
        SetGarrison(forge, 1);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, forge.Id, 10)));
        var army = match.ArmiesInFlight.Single();

        // Read before the arrival resolves (FR-3): both terms are live, and ownership of the forge
        // is exactly what this capture is about to change.
        var attackerForgePercent = match.ForgeAttackPercentFor(match.HumanPlayer);
        var defenderForgePercent = match.ForgeDefencePercentFor(forge.Owner);

        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, forge.Owner);

        // 10 attackers vs 1 defender at identity indices (100/100, and a neutral forge buffs nobody
        // - D-47) captures outright, killing 0 attackers (Bu=1 < Wu=10 leaves the whole wave alive)
        // - no AttackingUnitDestroyedGain/Loss swing to net against the capture gain.
        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0), attackerForgePercent);
        var defenderIndex = CombatResolver.ComposeDefenderIndex(forge.DefencePercentage, moraleDefencePercent: 100, defenderForgePercent);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, 10, 1);
        Assert.True(result.Captured); // sanity
        var attackerDeaths = 10 - result.RemainingGarrison;

        Assert.Equal(200 - (attackerDeaths * MoraleTable.AttackingUnitDestroyedGain), match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);
    }

    /// <summary>
    /// Capturing an opponent's forge awards the capturer 300 and charges the previous owner 100, in
    /// the same single clamped write per player the existing capture path performs (D-38).
    /// </summary>
    [Fact]
    public void CapturingAnOpponentsForge_Awards300_Charges100()
    {
        var match = new Match(_layoutWithNeutralForge);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var forge = match.Bases.Single(b => b.Type == BaseType.Forge);
        SetOwner(forge, match.AiPlayer);
        SetGarrison(human, 40);
        SetGarrison(forge, 1);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, forge.Id, 10)));
        var army = match.ArmiesInFlight.Single();

        // Read before the arrival resolves (FR-3). The AI holds this forge at combat time, so its
        // own defence term is ForgeTable's one-forge 125% - a forge defends itself.
        var attackerForgePercent = match.ForgeAttackPercentFor(match.HumanPlayer);
        var defenderForgePercent = match.ForgeDefencePercentFor(forge.Owner);
        Assert.Equal(ForgeTable.DefencePercentage(1), defenderForgePercent);

        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, forge.Owner);

        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0), attackerForgePercent);
        var defenderIndex = CombatResolver.ComposeDefenderIndex(forge.DefencePercentage, moraleDefencePercent: 100, defenderForgePercent);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, 10, 1);
        Assert.True(result.Captured); // sanity
        var attackerDeaths = 10 - result.RemainingGarrison;
        var deathSwing = attackerDeaths * MoraleTable.AttackingUnitDestroyedGain;

        Assert.Equal(300 - deathSwing, match.HumanMorale.Points);
        Assert.Equal(MoraleTable.ClampPoints(-100 + deathSwing), match.AiMorale.Points);
    }

    /// <summary>The standing asymmetry: 300 gained exceeds 100 lost, so a forge trade is net-positive for the aggressor.</summary>
    [Fact]
    public void OpponentForgeCapture_GainExceedsLoss()
    {
        Assert.True(MoraleTable.Forge.CaptureGain(LevelTable.MinLevel, wasOpponentOwned: true) > MoraleTable.Forge.CaptureLoss(LevelTable.MinLevel));
    }

    /// <summary>
    /// Completing a conversion into a forge awards no morale, and neither does converting out of
    /// one - conversion is not an upgrade, and <see cref="Match"/>'s conversion-completion branch
    /// awards nothing today and must still award nothing after this feature.
    /// </summary>
    [Fact]
    public void CompletingAConversionIntoOrOutOfAForge_AwardsNoMorale()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        SetGarrison(human, 70);

        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Forge)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Forge, human.Type);
        Assert.Equal(0, match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);

        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Producer)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Producer, human.Type);
        Assert.Equal(0, match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);
    }

    /// <summary>
    /// Recapturing a forge inside FR-3c's grace window scores the same as any other capture of it -
    /// the grace suppresses a level demotion, and a forge has no level to demote, so the morale
    /// values are unaffected.
    /// </summary>
    [Fact]
    public void RecapturingAForgeWithinGrace_ScoresTheSameAsAnyOtherCapture()
    {
        var match = new Match(_layoutWithNeutralForge);
        var forge = match.Bases.Single(b => b.Type == BaseType.Forge);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetOwner(forge, match.HumanPlayer);
        SetGarrison(forge, 1);
        SetOwnerBeforeLastChange(forge, match.AiPlayer); // the AI held it immediately before the human
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, forge.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(forge, arrivalTick - 5); // well inside the grace window

        // Read before the arrival resolves (FR-3): the human holds this forge at combat time.
        var attackerForgePercent = match.ForgeAttackPercentFor(match.AiPlayer);
        var defenderForgePercent = match.ForgeDefencePercentFor(forge.Owner);

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, forge.Owner); // a true retake
        Assert.Equal(LevelTable.MinLevel, forge.Level); // no level to demote in the first place

        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0), attackerForgePercent);
        var defenderIndex = CombatResolver.ComposeDefenderIndex(forge.DefencePercentage, moraleDefencePercent: 100, defenderForgePercent);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, 10, 1);
        var attackerDeaths = 10 - result.RemainingGarrison;
        var deathSwing = attackerDeaths * MoraleTable.AttackingUnitDestroyedGain;

        // The AI captures back an opponent(human)-owned forge: +300 to the AI, -100 to the human,
        // netted against the attacking-unit death swing exactly as any other capture.
        Assert.Equal(300 - deathSwing, match.AiMorale.Points);
        Assert.Equal(MoraleTable.ClampPoints(-100 + deathSwing), match.HumanMorale.Points);
    }

    /// <summary>
    /// Determinism holds (D-12, S-8): the same commands replayed against the same starting state,
    /// chunked differently across <see cref="Match.Advance"/> calls, produce identical morale points
    /// for both players on every tick, including across a forge capture.
    /// </summary>
    [Fact]
    public void ForgeCapture_IsDeterministic_AcrossDifferentAdvanceChunking()
    {
        Match Build()
        {
            var m = new Match(_layoutWithNeutralForge);
            var human = m.Bases.Single(b => b.Owner == m.HumanPlayer);
            var forge = m.Bases.Single(b => b.Type == BaseType.Forge);
            SetOwner(forge, m.AiPlayer);
            SetGarrison(human, 40);
            SetGarrison(forge, 1);
            Assert.Equal(SendArmyOutcome.Accepted, m.Execute(new SendArmyCommand(m.HumanPlayer, human.Id, forge.Id, 10)));
            return m;
        }

        var oneShot = Build();
        var arrivalTick = oneShot.ArmiesInFlight.Single().ArrivalTick;
        oneShot.Advance(arrivalTick - oneShot.ElapsedTicks);

        var chunked = Build();
        for (var i = 0; i < arrivalTick; i++)
        {
            chunked.Advance(1);
        }

        Assert.Equal(oneShot.HumanMorale.Points, chunked.HumanMorale.Points);
        Assert.Equal(oneShot.AiMorale.Points, chunked.AiMorale.Points);
    }

    /// <summary>
    /// A match in which no forge ever exists produces today's morale numbers exactly - the
    /// zero-forge baseline, now literally true of <see cref="MapCatalog.Small"/> (the parameterless
    /// constructor's default as of phase 7 FR-2), which carries no forge at all.
    /// </summary>
    [Fact]
    public void ZeroForgeBaseline_ProducesTodaysMoraleNumbersExactly()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 30);
        SetGarrison(neutral, 5);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        // Neither player owns a forge here - Small has no forge slot at all - so both terms are
        // ForgeTable's identity.
        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0), ForgeTable.AttackPercentage(ForgeTable.MinForgeCount));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(neutral.DefencePercentage, moraleDefencePercent: 100, ForgeTable.DefencePercentage(ForgeTable.MinForgeCount));
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, 6, 5);
        var attackerDeaths = result.Captured ? 6 - result.RemainingGarrison : 6;
        var deathSwing = attackerDeaths * MoraleTable.AttackingUnitDestroyedGain;
        var expectedGain = result.Captured
            ? MoraleTable.Village.CaptureGain(LevelTable.MinLevel, wasOpponentOwned: false)
            : 0;

        Assert.Equal(MoraleTable.ClampPoints(expectedGain - deathSwing), match.HumanMorale.Points);
    }
}
