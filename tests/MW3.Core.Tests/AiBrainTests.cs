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

    // --- Clause 3: attack ---

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

    // --- Clause 4: consolidate ---

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
    /// any kind, for either a send or an upgrade.
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
