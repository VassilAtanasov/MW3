using System.Reflection;

namespace MW3.Core.Tests;

public class AiBrainTests
{
    private static BrainDecision InvokeClause(string methodName, AiBrain brain, Match match, List<Base> ownBases)
    {
        var method = typeof(AiBrain).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (BrainDecision)method.Invoke(brain, new object[] { match, ownBases })!;
    }

    private static List<Base> OwnBases(Match match, Player player) =>
        match.Bases.Where(b => b.Owner == player).OrderBy(b => b.Id).ToList();

    // --- Clause 1: defend ---

    [Fact]
    public void TryDefend_ThreatenedBase_IsReinforcedFromASourceThatCanArriveInTime()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // arrives tick 34 + 76 = 110

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(ai, decision.Command.IssuingPlayer);
        Assert.Equal(neutral5.Id, decision.Command.SourceBaseId);
        Assert.Equal(aiBase.Id, decision.Command.TargetBaseId);
        Assert.Equal(1, decision.Command.UnitCount); // floor(1/2) clamped to 1
    }

    [Fact]
    public void TryDefend_ThreatenedBase_NotReinforcedWhenNoSourceCanArriveInTime()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // arrives tick 110

        match.Advance(74); // elapsed 108: only 2 ticks remain before arrival - too little for base 5 (34 ticks away)

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryDefend_NoDoubleTargeting_SkipsAThreatenedBaseAlreadyBeingReinforced()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // threat, arrives tick 110
        match.Execute(new SendArmyCommand(ai, neutral5.Id, aiBase.Id, 1)); // already reinforcing

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    // --- Clause 2: upgrade ---

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetType(Base b, BaseType type) =>
        typeof(Base).GetProperty(nameof(Base.Type))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { type });

    [Fact]
    public void TryUpgrade_SaturatedCandidate_ProducesAnUpgradeCommand_ForThatBase()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsUpgrade);
        Assert.Equal(ai, decision.Upgrade.IssuingPlayer);
        Assert.Equal(aiBase.Id, decision.Upgrade.BaseId);
    }

    [Fact]
    public void TryUpgrade_YieldsNothing_WhenNoBaseIsAtCap()
    {
        var match = new Match();
        var ai = match.AiPlayer;

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryUpgrade_ATower_IsNeverACandidate_EvenWhenAboveWhatWouldBeItsCap()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetType(aiBase, BaseType.Tower); // GarrisonCap is null for a tower (D-28) - the empty case
        SetGarrison(aiBase, 999);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryUpgrade_UnderConstruction_IsNotACandidate()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(ai, aiBase.Id)));
        // Restore the garrison to at-cap after the cost was paid, so saturation alone isn't what
        // disqualifies it - only the in-progress construction should.
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value);
        Assert.NotNull(aiBase.Construction);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryUpgrade_AtMaxUpgradableLevel_IsNotACandidate()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetLevel(aiBase, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MaxUpgradableLevel(BaseType.Producer))!.Value);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryUpgrade_ThreatenedBase_IsNotACandidate_EvenWhenSaturatedAndAffordable()
    {
        // D-30: the upgrade's cost is deducted immediately while the level lands 100+ ticks later,
        // so a base upgrading under attack can hand over a capture it would otherwise have held.
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var humanBase = match.Bases.Single(b => b.Owner == human);
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value);

        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 1)); // in flight, any size

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryUpgrade_AmongSaturatedCandidates_PicksTheOneFurthestFromTheNearestNotOwnedBase()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        // Both saturated: aiBase (id 1)'s nearest not-owned base is id 4 at ~0.34, neutral5 (id 5,
        // now AI-owned)'s nearest not-owned base is id 3 at ~0.30 - aiBase is the safer (furthest)
        // candidate, exactly the mirror of the consolidate test that picks id 5 as the nearer front.
        var cap = LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value;
        SetGarrison(aiBase, cap);
        SetGarrison(neutral5, cap);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(aiBase.Id, decision.Upgrade.BaseId);
    }

    [Fact]
    public void TryUpgrade_TiedDistance_BreaksTowardsTheLowerId()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral4 = match.Bases[4];
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral4.Id, 6)); // captures at tick t with 1 remaining
        var army1 = match.ArmiesInFlight.Single();
        match.Advance(army1.ArrivalTick - match.ElapsedTicks);

        SetGarrison(aiBase, 6); // regrown enough to also take neutral5, without depending on exact ticks
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures with 1 remaining
        var army2 = match.ArmiesInFlight.Single();
        match.Advance(army2.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(ai, neutral4.Owner);
        Assert.Equal(ai, neutral5.Owner);

        SetGarrison(aiBase, 0); // not a candidate: below its cap

        // id 4 (0.65, 0.25) and id 5 (0.65, 0.75) are each 0.30 from their own nearest not-owned base
        // (id 2 and id 3 respectively) - an exact tie, broken towards the lower id.
        var cap = LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value;
        SetGarrison(neutral4, cap);
        SetGarrison(neutral5, cap);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryUpgrade", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(neutral4.Id, decision.Upgrade.BaseId);
    }

    [Fact]
    public void Decide_UpgradeOutranksAttack_WhenACapturedBaseIsSaturated()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);

        match.Advance(300); // uncontested growth: aiBase would otherwise attack (existing attack test)
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value);

        var brain = new AiBrain(ai);
        var decision = brain.Decide(match);

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsUpgrade);
        Assert.Equal(aiBase.Id, decision.Upgrade.BaseId);
    }

    // --- Clause 3: convert ---

    [Fact]
    public void TryConvert_SaturatedMaxLevelCandidate_ProducesAConvertCommand_ForThatBase()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        // aiBase (id 1) is the only saturated-at-max-level candidate; neutral5 (id 5, now AI-owned)
        // has only 1 garrison, well below LevelTable.ConversionCost - not a candidate at all.
        SetLevel(aiBase, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MaxUpgradableLevel(BaseType.Producer))!.Value);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsConvert);
        Assert.Equal(ai, decision.Convert.IssuingPlayer);
        Assert.Equal(aiBase.Id, decision.Convert.BaseId);
        Assert.Equal(BaseType.Tower, decision.Convert.TargetType);
    }

    [Fact]
    public void TryConvert_YieldsNothing_WhenNoBaseIsAtOrAboveTheConversionCost()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);
        SetGarrison(aiBase, LevelTable.ConversionCost - 1); // just under the cost

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConvert_ATower_IsNeverACandidate_EvenWhenGarrisonExceedsTheConversionCost()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);
        SetType(aiBase, BaseType.Tower);
        SetGarrison(aiBase, 999);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConvert_UnderConstruction_IsNotACandidate()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);
        SetGarrison(aiBase, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(ai, aiBase.Id, BaseType.Tower)));
        SetGarrison(aiBase, LevelTable.ConversionCost + 20); // restore: saturation alone shouldn't disqualify it
        Assert.NotNull(aiBase.Construction);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConvert_ThreatenedCandidate_IsNotACandidate_EvenWhenAffordable()
    {
        // D-30: the conversion's cost is deducted immediately while the type change lands 100 ticks
        // later, so converting under attack can hand over a capture it would otherwise have held.
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var humanBase = match.Bases.Single(b => b.Owner == human);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);
        SetGarrison(aiBase, LevelTable.ConversionCost + 20);

        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 1)); // in flight, any size

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConvert_NeverProducesACommand_WhenTheAiOwnsOnlyOneBase()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetGarrison(aiBase, 999); // saturated well past the conversion cost, but the AI's only base

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConvert_AmongCandidates_PicksTheOneNearestTheFront_MirroringConsolidate()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral4 = match.Bases[4];
        var neutral5 = match.Bases[5];

        // Capture both flank neutrals so the AI has three candidates to choose among.
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral4.Id, 6));
        var army1 = match.ArmiesInFlight.Single();
        match.Advance(army1.ArrivalTick - match.ElapsedTicks);

        SetGarrison(aiBase, 6);
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        var army2 = match.ArmiesInFlight.Single();
        match.Advance(army2.ArrivalTick - match.ElapsedTicks);

        // aiBase (id 1)'s nearest not-owned base (id 4 or 5, whichever wasn't captured - here
        // neither, both are now owned) is id 0.34 away via id2/id3; neutral4 and neutral5 are each
        // 0.30 away from their own nearest not-owned neighbor (id 2 and id 3 respectively) - nearer
        // than aiBase, so one of them is the front. All three are made candidates; the nearer one
        // (lower id on a tie) wins, exactly TryConsolidate's own tie-break.
        SetGarrison(aiBase, LevelTable.ConversionCost + 5);
        SetGarrison(neutral4, LevelTable.ConversionCost + 5);
        SetGarrison(neutral5, LevelTable.ConversionCost + 5);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConvert", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsConvert);
        Assert.Equal(neutral4.Id, decision.Convert.BaseId); // 0.30 away, tied with neutral5, lower id wins
    }

    [Fact]
    public void TryConvertDecision_WhenExecuted_ConvertsTheBaseAndDropsGarrisonByExactlyTheConversionCost()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral5 = match.Bases[5];

        // TryConvert's own guard requires at least two owned bases (converting the AI's only base
        // would remove its sole source of new units) - capture a second one first.
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        SetLevel(aiBase, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MaxUpgradableLevel(BaseType.Producer))!.Value);

        var brain = new AiBrain(ai);
        var decision = brain.Decide(match);
        Assert.True(decision.HasCommand);
        Assert.True(decision.IsConvert);

        var garrisonBeforeConvert = aiBase.GarrisonCount;
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(decision.Convert));
        Assert.Equal(garrisonBeforeConvert - LevelTable.ConversionCost, aiBase.GarrisonCount);

        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(BaseType.Tower, aiBase.Type);
        Assert.Equal(LevelTable.MinLevel, aiBase.Level);
    }

    [Fact]
    public void Decide_SingleBase_SaturatedWellPastTheConversionCost_UpgradesInsteadOfConverting()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        SetGarrison(aiBase, 999); // saturated well past LevelTable.ConversionCost (30), the AI's only base

        var brain = new AiBrain(ai);
        var decision = brain.Decide(match);

        Assert.True(decision.HasCommand);
        Assert.True(decision.IsUpgrade);
        Assert.False(decision.IsConvert);
        Assert.Equal(aiBase.Id, decision.Upgrade.BaseId);
    }

    // --- PredictGarrison ---

    [Fact]
    public void PredictGarrison_ATower_NeverGrows_EvenFarIntoTheFuture()
    {
        var match = new Match();
        var human = match.HumanPlayer;
        var humanBase = match.Bases.Single(b => b.Owner == human);
        SetType(humanBase, BaseType.Tower);
        SetGarrison(humanBase, 7);

        var method = typeof(AiBrain).GetMethod("PredictGarrison", BindingFlags.NonPublic | BindingFlags.Static)!;
        var predicted = (int)method.Invoke(null, new object[] { humanBase, match.ElapsedTicks, match.ElapsedTicks + 100_000 })!;

        Assert.Equal(7, predicted);
    }

    // --- Clause 4: attack ---

    [Fact]
    public void TryAttack_ChoosesTheNearerWinnableTarget_OverAFurtherWinnableOne()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);

        match.Advance(300); // uncontested growth at 60 ticks/unit: aiBase garrison becomes 10 + 5 = 15

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(aiBase.Id, decision.Command.SourceBaseId);
        Assert.Equal(4, decision.Command.TargetBaseId); // nearest neutral (id 4), not the farther id 2/3
        Assert.Equal(7, decision.Command.UnitCount); // floor(15 / 2)
    }

    [Fact]
    public void TryAttack_DeclinesATarget_WhenTheProductionAdjustedArrivalGarrisonMakesItUnwinnable()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var humanBase = match.Bases.Single(b => b.Owner == human);
        var neutral2 = match.Bases[2];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 3)); // decoy: sheds garrison, safely repelled rather than capturing
        match.Execute(new SendArmyCommand(human, humanBase.Id, neutral2.Id, 6)); // captures neutral 2 at tick 34 with 1 remaining

        match.Advance(290);

        // aiBase: 10 - 3 (decoy) + 4 (production, 290 ticks at 60/unit) = 11. floor(11/2) = 5.
        // neutral 2 (now human-owned): captured with 1 at tick 34; by the time an army launched now
        // would arrive (tick 290 + 59 = 349), five more production periods have grown it to 6 - not
        // winnable against 5, even though its current count (lower) would have looked winnable.
        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryAttack_NoDoubleTargeting_SkipsAnAlreadyTargetedBase_AndPicksTheNextWinnableOne()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);

        match.Advance(300); // aiBase garrison becomes 15
        match.Execute(new SendArmyCommand(ai, aiBase.Id, 4, 1)); // decoy already targeting the nearest neutral (id 4)

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(5, decision.Command.TargetBaseId); // id 4 skipped (already targeted); id 5 ties its distance
        Assert.Equal(7, decision.Command.UnitCount); // floor(14 / 2) after the 1-unit decoy left
    }

    // --- Clause 4: attack, routing around enemy tower fire (FR-7) ---

    [Fact]
    public void TryAttack_AmongTwoEquallyWinnableTargets_PrefersTheOneThatAvoidsAnEnemyTower()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral4 = match.Bases[4]; // tied distance with neutral5 from aiBase
        var neutral5 = match.Bases[5];

        // neutral4 becomes a level-1 enemy tower: an army flying straight at it from aiBase spends
        // most of the final stretch inside its own range, losing an estimated 3 units (see
        // TowerThreatEstimator). neutral5 sits far outside any tower's range on this map (ranges are
        // deliberately kept below the map's minimum base-to-base distance), so attacking it costs
        // nothing. Both start with the same garrison (5), so both are equally winnable ignoring
        // tower losses - weighing a level-1 tower's own 140% defence (#68), not 100%.
        SetOwner(neutral4, human);
        SetType(neutral4, BaseType.Tower);
        SetLevel(neutral4, LevelTable.MinLevel);
        SetGarrison(neutral4, 5);

        // Large enough that unclampedHalf (11), minus the estimated 3-unit tower loss, still exceeds
        // neutral4's 700 (5 garrison x 140% defence) threshold (8 x 100 = 800), and neutral5 - no
        // loss on that path - is winnable many times over (11 x 100 = 1100 vs 5 x 100 = 500). Both
        // targets stay genuinely winnable, so this proves a preference, not a refusal.
        SetGarrison(aiBase, 22);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(neutral5.Id, decision.Command.TargetBaseId);
    }

    [Fact]
    public void TryAttack_OnlyViableTargetBehindATower_IsStillAttacked_WhenWinnableAfterTheEstimatedLoss()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral4 = match.Bases[4];

        MakeOnlyNeutral4Viable(match, human, neutral4);
        // unclampedHalf 11, minus the ~3-unit estimated loss, is 8: 8 x 100 = 800 > neutral4's
        // 5 garrison x 140% (a level-1 tower's own defence, #68) = 700.
        SetGarrison(aiBase, 22);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(neutral4.Id, decision.Command.TargetBaseId);
    }

    [Fact]
    public void TryAttack_OnlyViableTargetBehindATower_IsDeclined_WhenUnwinnableAfterTheEstimatedLoss()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral4 = match.Bases[4];

        MakeOnlyNeutral4Viable(match, human, neutral4);
        // unclampedHalf 10, minus the ~3-unit estimated loss, is 7: 7 x 100 = 700 is an exact tie
        // with neutral4's 5 garrison x 140% defence (#68), and a tie does not capture (CombatResolver).
        SetGarrison(aiBase, 20);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    /// <summary>
    /// #68's own worked example: a level-3 village (120% defence) holding 12 units is not winnable
    /// with 13 attacking units (13 × 100 = 1300 ≤ 12 × 120 = 1440), even though 13 raw units
    /// outnumber its 12 - the old comparison would have attacked and lost all 13 for nothing.
    /// </summary>
    [Fact]
    public void TryAttack_Declines_WhenDefencePercentageMakesA13UnitAttackUnwinnableAgainstA12UnitLevel3Village()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral4 = match.Bases[4];

        MakeOnlyOneTargetViable(match, neutral4);
        SetLevel(neutral4, 3);
        SetGarrison(neutral4, 12);

        SetGarrison(aiBase, 26); // unclampedHalf = floor(26 * 50 / 100) = 13, no tower loss on this map

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    /// <summary>
    /// The mirror of the test above: 15 attacking units against the same 120%-defended, 12-unit
    /// village succeed (15 × 100 = 1500 &gt; 12 × 120 = 1440), proving the fix suppresses only the
    /// genuinely unwinnable attack rather than every attack on a higher-level village.
    /// </summary>
    [Fact]
    public void TryAttack_Accepts_WhenDefencePercentageStillMakesA15UnitAttackWinnableAgainstA12UnitLevel3Village()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral4 = match.Bases[4];

        MakeOnlyOneTargetViable(match, neutral4);
        SetLevel(neutral4, 3);
        SetGarrison(neutral4, 12);

        SetGarrison(aiBase, 30); // unclampedHalf = floor(30 * 50 / 100) = 15

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(neutral4.Id, decision.Command.TargetBaseId);
        Assert.Equal(15, decision.Command.UnitCount);
    }

    /// <summary>
    /// A source with 0 or 1 garrison must never be winnable, even against an empty target: the
    /// winnability check uses the unclamped half-garrison (floor, no minimum-1), unlike the size
    /// <see cref="SendStrengthCalculator"/> computes for the eventual send (FR-1). A source at 1
    /// garrison unclamps to 0, so <c>0 - expectedTowerLoss</c> can never exceed a non-negative
    /// predicted garrison - not even the weakest possible target, garrison 0.
    /// </summary>
    [Fact]
    public void TryAttack_Declines_WhenSourceGarrisonIsOneAndTargetIsEmpty()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var emptyNeutral = match.Bases[2];

        SetGarrison(aiBase, 1);
        SetGarrison(emptyNeutral, 0);

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    /// <summary>
    /// Sets up every other base on the map as unwinnable, leaving neutral4 - a level-1 enemy tower
    /// with garrison 5 - the AI's only viable attack candidate from its own base.
    /// </summary>
    private static void MakeOnlyNeutral4Viable(Match match, Player human, Base neutral4)
    {
        SetOwner(neutral4, human);
        SetType(neutral4, BaseType.Tower);
        SetLevel(neutral4, LevelTable.MinLevel);
        SetGarrison(neutral4, 5);

        foreach (var other in match.Bases.Where(b => b.Id != neutral4.Id && b.Owner != match.AiPlayer))
        {
            SetGarrison(other, 1000); // unwinnable regardless of unclampedHalf
        }
    }

    /// <summary>
    /// Sets every other non-owned base's garrison sky-high so <paramref name="target"/> - left a
    /// neutral village at its default level - is the AI's only viable attack candidate, without
    /// otherwise touching its type, level, or garrison (the caller sets those to fit its scenario).
    /// </summary>
    private static void MakeOnlyOneTargetViable(Match match, Base target)
    {
        foreach (var other in match.Bases.Where(b => b.Id != target.Id && b.Owner != match.AiPlayer))
        {
            SetGarrison(other, 1000); // unwinnable regardless of unclampedHalf
        }
    }

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    // --- Clause 5: consolidate ---

    [Fact]
    public void TryConsolidate_SkippedWhenTheAiOwnsFewerThanTwoBases()
    {
        var match = new Match();
        var ai = match.AiPlayer;

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConsolidate", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void Decide_ConsolidatesFromTheLargestOtherBaseToTheFront_WhenNothingToDefendOrWin()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        // aiBase (id 1): garrison 4 (10 - 6, no production yet at 60 ticks/unit), nearest non-owned
        // base (id 4) is 34 ticks away.
        // neutral5 (id 5, now AI-owned): garrison 1, nearest non-owned base (id 3) is 30 ticks away - the front.
        var brain = new AiBrain(ai);
        var decision = brain.Decide(match);

        Assert.True(decision.HasCommand);
        Assert.Equal(aiBase.Id, decision.Command.SourceBaseId);
        Assert.Equal(neutral5.Id, decision.Command.TargetBaseId);
        Assert.Equal(2, decision.Command.UnitCount); // floor(4 / 2)
    }

    [Fact]
    public void TryConsolidate_NoDoubleTargeting_SkipsAFrontAlreadyBeingFed()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(34);
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 1)); // already feeding the front

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConsolidate", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryConsolidate_NeverPicksAZeroGarrisonBaseAsSource()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai); // id 1
        var neutral4 = match.Bases[4];
        var neutral2 = match.Bases[2];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral4.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);

        // Drain aiBase (id 1) to exactly zero: a repelled tie (N == M) leaves a base owned but
        // empty (Match.ResolveArrival), so a source candidate reaching zero is a real, reachable
        // state - not just a hand-picked test fixture.
        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral2.Id, aiBase.GarrisonCount));
        Assert.Equal(0, aiBase.GarrisonCount);

        // aiBase (id 1, garrison 0) is the only base other than the front (id 4, nearer any
        // non-owned base than id 1 is); with no non-zero source available, consolidation must
        // yield nothing rather than issue a command Match.Execute would reject.
        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryConsolidate", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    /// <summary>
    /// #68: the threat check now weighs the candidate's own <see cref="Base.DefencePercentage"/>
    /// rather than comparing raw unit counts. A level-3 base (120% defence) holding 12 units is not
    /// threatened by a 13-unit attack (13 × 100 = 1300 ≤ 12 × 120 = 1440) even though 13 raw units
    /// outnumber its 12 - the old comparison would have flagged this as threatened.
    /// </summary>
    [Fact]
    public void TryDefend_NotThreatened_WhenTheDefencePercentageWouldHoldDespiteRawOutnumbering()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var humanBase = match.Bases.Single(b => b.Owner == human);

        SetLevel(aiBase, 3);
        SetGarrison(aiBase, 12);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 13));

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    [Fact]
    public void TryDefend_NeverPicksAZeroGarrisonBaseAsSource()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];
        var neutral3 = match.Bases[3];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 34 with 1 remaining
        match.Advance(34);
        match.Execute(new SendArmyCommand(ai, neutral5.Id, neutral3.Id, neutral5.GarrisonCount)); // drains it to zero
        Assert.Equal(0, neutral5.GarrisonCount);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // threat, arrives tick 110

        // base 5 (the AI's only other base) can arrive in time but has zero garrison, so it must
        // not be picked as the reinforcement source.
        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    // --- Cross-cutting ---

    [Fact]
    public void Decide_NeverProducesACommandIssuedByAnyoneOtherThanTheAi()
    {
        var match = new Match();
        var brain = new AiBrain(match.AiPlayer);

        for (var tick = MatchRunner.DecisionIntervalTicks; tick <= 2000; tick += MatchRunner.DecisionIntervalTicks)
        {
            match.Advance(tick - match.ElapsedTicks);
            var decision = brain.Decide(match);
            if (!decision.HasCommand)
            {
                continue;
            }

            if (decision.IsUpgrade)
            {
                Assert.Equal(match.AiPlayer, decision.Upgrade.IssuingPlayer);
                match.Execute(decision.Upgrade);
            }
            else if (decision.IsConvert)
            {
                Assert.Equal(match.AiPlayer, decision.Convert.IssuingPlayer);
                match.Execute(decision.Convert);
            }
            else
            {
                Assert.Equal(match.AiPlayer, decision.Command.IssuingPlayer);
                match.Execute(decision.Command);
            }
        }
    }

    [Fact]
    public void EveryAiCommand_OverFiveThousandTicks_IsAlwaysAccepted()
    {
        var match = new Match();
        var brain = new AiBrain(match.AiPlayer);

        for (var tick = MatchRunner.DecisionIntervalTicks; tick <= 5000; tick += MatchRunner.DecisionIntervalTicks)
        {
            match.Advance(tick - match.ElapsedTicks);
            var decision = brain.Decide(match);
            if (!decision.HasCommand)
            {
                continue;
            }

            if (decision.IsUpgrade)
            {
                var upgradeOutcome = match.Execute(decision.Upgrade);
                Assert.Equal(UpgradeOutcome.Accepted, upgradeOutcome);
            }
            else if (decision.IsConvert)
            {
                var convertOutcome = match.Execute(decision.Convert);
                Assert.Equal(ConvertOutcome.Accepted, convertOutcome);
            }
            else
            {
                var sendOutcome = match.Execute(decision.Command);
                Assert.Equal(SendArmyOutcome.Accepted, sendOutcome);
            }
        }
    }

    /// <summary>
    /// An <see cref="IPlayerBrain"/> that decides with a real <see cref="AiBrain"/>, then - instead
    /// of letting <see cref="MatchRunner"/> submit the decision itself - submits it right here
    /// through <see cref="MatchRunner.Execute(SendArmyCommand)"/> / <see cref="MatchRunner.Execute(UpgradeCommand)"/>
    /// (the same single path every command takes) and asserts it was accepted, before returning
    /// <see cref="BrainDecision.None"/> so the runner does not submit it a second time. This is what
    /// lets a test observe every AI command's outcome while still running the real
    /// <see cref="MatchRunner"/> stepping algorithm end to end.
    /// </summary>
    private sealed class AssertingBrain : IPlayerBrain
    {
        private readonly AiBrain _inner;

        public AssertingBrain(Player player) => _inner = new AiBrain(player);

        public Player Player => _inner.Player;

        public MatchRunner? Runner { get; set; }

        public BrainDecision Decide(Match match)
        {
            var decision = _inner.Decide(match);
            if (decision.HasCommand)
            {
                Assert.NotNull(Runner);
                if (decision.IsUpgrade)
                {
                    Assert.Equal(UpgradeOutcome.Accepted, Runner!.Execute(decision.Upgrade));
                }
                else if (decision.IsConvert)
                {
                    Assert.Equal(ConvertOutcome.Accepted, Runner!.Execute(decision.Convert));
                }
                else
                {
                    Assert.Equal(SendArmyOutcome.Accepted, Runner!.Execute(decision.Command));
                }
            }

            return BrainDecision.None;
        }
    }

    /// <summary>
    /// Mirrors the standing convention from phase 2's issue #24 finding, restated for the widened
    /// decision shape: every command the brain produces over a full headless match, run through
    /// <see cref="MatchRunner"/> exactly as the real game would, must be accepted - no rejection of
    /// any kind, for a send, an upgrade, or a convert (FR-7).
    /// </summary>
    [Fact]
    public void EveryAiDecision_OverAFullHeadlessMatch_ThroughMatchRunner_IsAlwaysAccepted()
    {
        var match = new Match();
        var brain = new AssertingBrain(match.AiPlayer);
        var runner = new MatchRunner(match, brain);
        brain.Runner = runner;

        for (var elapsed = 0L; elapsed < 5000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);
        }
    }
}
