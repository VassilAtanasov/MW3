namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-2: the shipped map's contested neutral forge (id 6) and neutral tower (id 7) - the
/// neutral tower firing at either player (D-47), morale attribution for its unowned kills, and
/// capture on both new bases. See issue #86's acceptance criteria.
/// </summary>
public class NeutralForgeAndTowerTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static Base NeutralForge(Match match) => match.Bases.Single(b => b.Type == BaseType.Forge);

    private static Base NeutralTower(Match match) => match.Bases.Single(b => b.Type == BaseType.Tower && b.Owner is null);

    /// <summary>
    /// The AI's tower-threat sum now counts a tower whose Owner is null: a route crossing the
    /// neutral tower's range is scored with non-zero expected loss, where a layout with no tower at
    /// all scores zero on the identical route - the before/after this criterion asks for, not just
    /// that the null-owner clause is gone.
    /// </summary>
    [Fact]
    public void AiBrain_TotalExpectedTowerLoss_CountsTheUnownedNeutralTower()
    {
        var towerFreeLayout = new[]
        {
            new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        };
        var withoutTower = new Match(towerFreeLayout);
        var withTower = new Match(); // the shipped layout, carrying the neutral tower from tick 0

        var method = typeof(AiBrain).GetMethod("TotalExpectedTowerLoss", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var aiStart = new MapPoint(0.88, 0.50);
        var target = new MapPoint(0.50, 0.80); // the neutral tower's own position - a route ending inside its own range

        var lossWithoutTower = (int)method.Invoke(new AiBrain(withoutTower.AiPlayer), new object[] { withoutTower, aiStart, target, Match.ArmySpeedUnitsPerTick })!;
        var lossWithTower = (int)method.Invoke(new AiBrain(withTower.AiPlayer), new object[] { withTower, aiStart, target, Match.ArmySpeedUnitsPerTick })!;

        Assert.Equal(0, lossWithoutTower);
        Assert.True(lossWithTower > 0);
    }

    /// <summary>
    /// The neutral tower fires at a human army in range, in a fresh match with no player-built
    /// tower.
    /// </summary>
    [Fact]
    public void NeutralTower_Fires_AtAHumanArmyInRange()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var base3 = match.Bases.Single(b => b.Id == 3); // 0.158 from the neutral tower - inside level-1 range

        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, base3.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.NotNull(NeutralTower(match).LastFireTick);
        Assert.True(army.UnitCount < 6);
    }

    /// <summary>The same guard, proven for an AI army - a one-sided assertion does not satisfy this criterion.</summary>
    [Fact]
    public void NeutralTower_Fires_AtAnAiArmyInRange()
    {
        var match = new Match();
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var base5 = match.Bases.Single(b => b.Id == 5); // 0.158 from the neutral tower - inside level-1 range

        SetGarrison(ai, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, base5.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.NotNull(NeutralTower(match).LastFireTick);
        Assert.True(army.UnitCount < 6);
    }

    /// <summary>
    /// No army with a null owner can exist in MW3 today (neutral bases never send), so this guard
    /// cannot be reached from a script - it is pinned directly by constructing that state through
    /// reflection, ahead of a later phase that might give neutrals a send.
    /// </summary>
    [Fact]
    public void NeutralTower_NeverFires_AtAnArmyWithNoOwner()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var base3 = match.Bases.Single(b => b.Id == 3);

        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, base3.Id, 6)));
        var army = match.ArmiesInFlight.Single();

        var ownerField = typeof(Army).GetField("<Owner>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ownerField.SetValue(army, null);

        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Null(NeutralTower(match).LastFireTick);
        Assert.Equal(6, army.UnitCount);
    }

    /// <summary>
    /// A tower-free layout still skips per-tick tower evaluation (the optimisation stays real there);
    /// the shipped layout, which carries the neutral tower from tick 0, evaluates on every tick. Both
    /// halves asserted (D-47).
    /// </summary>
    [Fact]
    public void TowerEvaluation_IsSkipped_OnlyForALayoutWithNoTowerAtAll()
    {
        var towerFreeLayout = new[]
        {
            new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        };
        var towerFree = new Match(towerFreeLayout);
        var shipped = new Match();

        var hasAnyOwnedTower = typeof(Match).GetMethod("HasAnyOwnedTower", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        Assert.False((bool)hasAnyOwnedTower.Invoke(towerFree, null)!);
        Assert.True((bool)hasAnyOwnedTower.Invoke(shipped, null)!);
    }

    /// <summary>
    /// A kill by the neutral tower charges the victim exactly AttackingUnitDiedLoss and awards no
    /// player anything - the non-victim's morale is unchanged too, since the award being absent is
    /// the point.
    /// </summary>
    [Fact]
    public void NeutralTowerKill_ChargesTheVictim_AwardsNobody()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var base3 = match.Bases.Single(b => b.Id == 3);

        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, base3.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.NotNull(NeutralTower(match).LastFireTick);
        Assert.Equal(MoraleTable.ClampPoints(-MoraleTable.AttackingUnitDiedLoss), match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);
    }

    /// <summary>Once a player captures the neutral tower, its kills award that player normally.</summary>
    [Fact]
    public void CapturedNeutralTower_AwardsItsNewOwner_OnTheSameTermsAsAConvertedTower()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var tower = NeutralTower(match);
        SetGarrison(tower, 1); // low enough to survive the tower's own self-fire during approach and still be captured

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, tower.Id, 8)));
        var captureArmy = match.ArmiesInFlight.Single();
        match.Advance(captureArmy.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, tower.Owner);

        // Reinforced well past what a 6-unit attack (even ignoring the tower's own self-fire losses
        // against it) could overcome, so the assertion below isolates the kill/morale attribution
        // this test is about, not a second capture.
        SetGarrison(tower, 40);
        var moraleAfterCapture = match.HumanMorale.Points;

        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        SetGarrison(ai, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, tower.Id, 6)));
        var attackArmy = match.ArmiesInFlight.Single();
        match.Advance(attackArmy.ArrivalTick - match.ElapsedTicks + 30);

        Assert.Equal(match.HumanPlayer, tower.Owner); // still held - the tower shot the attack down
        Assert.True(match.HumanMorale.Points > moraleAfterCapture); // the new owner was awarded for the kill
    }

    /// <summary>
    /// Capturing the neutral forge awards +200 (FR-4's neutral-forge row) and charges nobody - there
    /// is no previous owner.
    /// </summary>
    [Fact]
    public void CapturingTheNeutralForge_Awards200_ChargesNobody()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var forge = NeutralForge(match);
        SetGarrison(forge, 5); // below the 8-unit single-wave ceiling's capture threshold
        var startingGarrison = forge.GarrisonCount;

        const int sentUnits = 6;
        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, forge.Id, sentUnits)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, forge.Owner);

        // The capture gain nets against the attacker's own dead-unit losses (D-41) - computed via
        // CombatResolver, the same single source ForgeMoraleTests' zero-forge-baseline test uses,
        // rather than hand-derived to avoid silently drifting from the resolver's own arithmetic.
        var attackerIndex = CombatResolver.ComposeAttackerIndex(MoraleTable.AttackPercentage(0));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(forge.DefencePercentage, moraleDefencePercent: 100);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, sentUnits, startingGarrison);
        var attackerDeaths = result.Captured ? sentUnits - result.RemainingGarrison : sentUnits;
        var expectedGain = MoraleTable.CaptureGain(BaseType.Forge, LevelTable.MinLevel, wasOpponentOwned: false)
            - (attackerDeaths * MoraleTable.AttackingUnitDestroyedGain);

        Assert.Equal(MoraleTable.ClampPoints(expectedGain), match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);
    }

    /// <summary>
    /// Capturing the neutral tower awards +80 - the existing level-1 neutral tower row, no new
    /// number. The net morale after capture also nets against the attacker's own dead-unit losses
    /// (D-41), which for this target includes the tower's own self-fire against its attacker during
    /// approach (D-47) - a second loss source arrival combat alone does not have - so this only
    /// asserts the constant itself and that the net stayed a genuine gain, rather than hand-deriving
    /// a number that depends on exactly how many shots landed before arrival.
    /// </summary>
    [Fact]
    public void CapturingTheNeutralTower_Awards80()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var tower = NeutralTower(match);
        SetGarrison(tower, 1); // low enough to survive the tower's own self-fire during approach and still be captured

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, tower.Id, 8)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, tower.Owner);
        Assert.Equal(80, MoraleTable.CaptureGain(BaseType.Tower, LevelTable.MinLevel, wasOpponentOwned: false));
        Assert.True(match.HumanMorale.Points > 0);
    }

    /// <summary>A captured neutral forge remains a Forge at its single tier (D-42) - never demoted, never destroyed.</summary>
    [Fact]
    public void CapturedNeutralForge_RemainsAForge()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var forge = NeutralForge(match);
        SetGarrison(forge, 5); // below the 8-unit single-wave ceiling's capture threshold

        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, forge.Id, 6)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, forge.Owner);
        Assert.Equal(BaseType.Forge, forge.Type);
        Assert.Equal(LevelTable.MinLevel, forge.Level);
    }

    /// <summary>A captured neutral tower remains a tower at level 1 and immediately fires for its new owner.</summary>
    [Fact]
    public void CapturedNeutralTower_RemainsATower_AndFiresImmediatelyForItsNewOwner()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var tower = NeutralTower(match);
        SetGarrison(tower, 1); // low enough to survive the tower's own self-fire during approach and still be captured

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, tower.Id, 8)));
        var captureArmy = match.ArmiesInFlight.Single();
        match.Advance(captureArmy.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, tower.Owner);
        Assert.Equal(BaseType.Tower, tower.Type);
        Assert.Equal(LevelTable.MinLevel, tower.Level);

        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        SetGarrison(ai, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, tower.Id, 6)));
        var attackArmy = match.ArmiesInFlight.Single();
        match.Advance(attackArmy.ArrivalTick - match.ElapsedTicks);

        Assert.True(attackArmy.UnitCount < 6); // fired for its new owner against the very next attacker
    }
}
