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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 17 with 1 remaining
        match.Advance(17);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // arrives tick 17 + 38 = 55

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
        match.Advance(17);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // arrives tick 55

        match.Advance(36); // elapsed 53: only 2 ticks remain before arrival - too little for base 5 (17 ticks away)

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
        match.Advance(17);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // threat, arrives tick 55
        match.Execute(new SendArmyCommand(ai, neutral5.Id, aiBase.Id, 1)); // already reinforcing

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryDefend", brain, match, OwnBases(match, ai));

        Assert.False(decision.HasCommand);
    }

    // --- Clause 2: attack ---

    [Fact]
    public void TryAttack_ChoosesTheNearerWinnableTarget_OverAFurtherWinnableOne()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);

        match.Advance(50); // uncontested growth: aiBase garrison becomes 10 + 5 = 15

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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 4)); // decoy: sheds garrison, doubles as a no-double-targeting guard on the far target
        match.Execute(new SendArmyCommand(human, humanBase.Id, neutral2.Id, 6)); // captures neutral 2 at tick 17 with 1 remaining

        match.Advance(17);

        // aiBase: 10 - 4 (decoy) + 1 (production) = 7. floor(7/2) = 3.
        // neutral 2 (now human-owned): current garrison 1; at arrival (tick 17 + 30 = 47) it has grown
        // by 3 production periods to 4 - naively "winnable" against the current 1, but not against 4.
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

        match.Advance(50); // aiBase garrison becomes 15
        match.Execute(new SendArmyCommand(ai, aiBase.Id, 4, 1)); // decoy already targeting the nearest neutral (id 4)

        var brain = new AiBrain(ai);
        var decision = InvokeClause("TryAttack", brain, match, OwnBases(match, ai));

        Assert.True(decision.HasCommand);
        Assert.Equal(5, decision.Command.TargetBaseId); // id 4 skipped (already targeted); id 5 ties its distance
        Assert.Equal(7, decision.Command.UnitCount); // floor(14 / 2) after the 1-unit decoy left
    }

    // --- Clause 3: consolidate ---

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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 17 with 1 remaining
        match.Advance(17);

        // aiBase (id 1): garrison 5, nearest non-owned base is 17 ticks away.
        // neutral5 (id 5, now AI-owned): garrison 1, nearest non-owned base (id 3) is 15 ticks away - the front.
        var brain = new AiBrain(ai);
        var decision = brain.Decide(match);

        Assert.True(decision.HasCommand);
        Assert.Equal(aiBase.Id, decision.Command.SourceBaseId);
        Assert.Equal(neutral5.Id, decision.Command.TargetBaseId);
        Assert.Equal(2, decision.Command.UnitCount); // floor(5 / 2)
    }

    [Fact]
    public void TryConsolidate_NoDoubleTargeting_SkipsAFrontAlreadyBeingFed()
    {
        var match = new Match();
        var ai = match.AiPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral5 = match.Bases[5];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6));
        match.Advance(17);
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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral4.Id, 6)); // captures at tick 17 with 1 remaining
        match.Advance(17);

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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)); // captures at tick 17 with 1 remaining
        match.Advance(17);
        match.Execute(new SendArmyCommand(ai, neutral5.Id, neutral3.Id, neutral5.GarrisonCount)); // drains it to zero
        Assert.Equal(0, neutral5.GarrisonCount);

        var humanBase = match.Bases.Single(b => b.Owner == human);
        match.Execute(new SendArmyCommand(human, humanBase.Id, aiBase.Id, 9)); // threat, arrives tick 55

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
            if (decision.HasCommand)
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
            if (decision.HasCommand)
            {
                var outcome = match.Execute(decision.Command);
                Assert.Equal(SendArmyOutcome.Accepted, outcome);
            }
        }
    }
}
