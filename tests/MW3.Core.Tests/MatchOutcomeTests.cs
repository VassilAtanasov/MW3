using System.Reflection;

namespace MW3.Core.Tests;

public class MatchOutcomeTests
{
    // A fixed sequence of human commands - discovered offline against the live AiBrain, then
    // hardcoded as data here (not a reactive strategy computed at runtime, which the AI heuristic
    // already covers and which a "brain for the human" is explicitly out of scope for) - proves
    // victory is actually attainable, not merely representable in the type system. Every count is
    // floor(source garrison / 2) at the moment of the send - identical to what a real drag produces
    // (MatchScreen.HandleDrag) - so this same sequence also backs qa/scripts/victory.txt. Reused by
    // every test below that needs a match to actually reach a decided outcome.
    //
    // Re-derived for #30: garrison caps changed the arithmetic every count in the phase-2 sequence
    // was tuned around, so it no longer reached victory. The cap is not a handicap peculiar to the
    // human - it throttles the AI's bases identically - so the winning shape is now to take and
    // hold the near flank early and feed a single staging base, rather than to out-accumulate the
    // opponent from one capital. The final send of 21 is above a single base's cap of 20 precisely
    // because arrivals stack above it (D-21); this sequence would be impossible under a hard cap.
    private static readonly (long Tick, int Source, int Target, int Count)[] _winningSequence =
    {
        (20, 0, 2, 6),
        (40, 0, 2, 4),
        (60, 0, 2, 3),
        (80, 2, 3, 6),
        (100, 0, 2, 3),
        (120, 2, 3, 6),
        (140, 0, 2, 4),
        (160, 2, 1, 7),
        (180, 0, 2, 4),
        (200, 3, 2, 5),
        (220, 2, 5, 9),
        (240, 0, 2, 5),
        (260, 3, 5, 5),
        (280, 2, 1, 11),
        (300, 2, 5, 7),
        (320, 0, 2, 6),
        (340, 3, 2, 7),
        (360, 1, 0, 5),
        (380, 0, 2, 6),
        (400, 0, 2, 7),
        (420, 3, 2, 7),
        (460, 1, 2, 7),
        (500, 5, 2, 9),
        (540, 2, 4, 21),
    };

    /// <summary>Submits every command in <see cref="_winningSequence"/>, at its exact tick, through <paramref name="runner"/>.</summary>
    private static void SubmitWinningSequence(Match match, MatchRunner runner)
    {
        foreach (var (tick, source, target, count) in _winningSequence)
        {
            runner.Advance(tick - match.ElapsedTicks);
            var outcome = runner.Execute(new SendArmyCommand(match.HumanPlayer, source, target, count));
            Assert.Equal(SendArmyOutcome.Accepted, outcome);
        }
    }

    [Fact]
    public void Outcome_StartsInProgress()
    {
        var match = new Match();

        Assert.Equal(MatchOutcome.InProgress, match.Outcome);
    }

    [Fact]
    public void OutcomeProperty_HasNoPublicSetter()
    {
        var property = typeof(Match).GetProperty(nameof(Match.Outcome))!;

        Assert.Null(property.GetSetMethod(nonPublic: false));
    }

    [Fact]
    public void PassiveHuman_ReachesHumanDefeat_WithinFiveThousandTicks_AiOwningEverything()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        for (var elapsed = 0L; elapsed < 5000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);
        }

        Assert.Equal(MatchOutcome.HumanDefeat, match.Outcome);
        Assert.All(match.Bases, b => Assert.Equal(match.AiPlayer, b.Owner));
    }

    [Fact]
    public void HandAuthoredHumanCommands_AgainstTheLiveAi_ReachHumanVictory()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(4500 - match.ElapsedTicks);

        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);
        Assert.All(match.Bases, b => Assert.Equal(match.HumanPlayer, b.Owner));
    }

    [Fact]
    public void NearMiss_ZeroBasesWithOneArmyInFlight_IsNotEliminated_AndSurvivesIfThatArmyCaptures()
    {
        var match = new Match();
        var human = match.HumanPlayer;
        var ai = match.AiPlayer;
        var humanBase = match.Bases.Single(b => b.Owner == human);
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var n4 = match.Bases[4];

        match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 10)); // arrives tick 38

        match.Advance(15);
        match.Execute(new SendArmyCommand(human, humanBase.Id, n4.Id, 7)); // arrives tick 45

        match.Advance(23); // elapsed 38: the AI captures the human's now-undefended capital
        Assert.Equal(ai, humanBase.Owner);
        Assert.Equal(MatchOutcome.InProgress, match.Outcome); // not eliminated - one army still in flight

        match.Advance(7); // elapsed 45: the human's own army lands and captures
        Assert.Equal(human, n4.Owner);
        Assert.Equal(MatchOutcome.InProgress, match.Outcome); // alive again - owns a base once more
    }

    [Fact]
    public void Outcome_SimultaneousElimination_DefeatTakesPrecedence()
    {
        // Ordinary play can never make both players own zero bases at once: a capture always
        // transfers ownership to the attacker (never to neither player), so the combined
        // human-plus-AI owned-base count is monotonically non-decreasing from its starting value
        // of 2. The precedence rule this test covers only matters for a state that has to be
        // constructed directly - reflection into Base.Owner's internal setter, the same style of
        // access this codebase already uses to reach otherwise-private extension points (e.g.
        // Match.ComputeTravelTicks in SendArmyTests).
        var match = new Match();

        var ownerSetter = typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!;
        foreach (var b in match.Bases)
        {
            ownerSetter.Invoke(b, new object?[] { null });
        }

        var evaluateOutcome = typeof(Match).GetMethod("EvaluateOutcome", BindingFlags.NonPublic | BindingFlags.Instance)!;
        evaluateOutcome.Invoke(match, null);

        Assert.Equal(MatchOutcome.HumanDefeat, match.Outcome);
    }

    [Fact]
    public void Outcome_NeutralBasesNeverAffectIt_FiveOwnedByHumanOneUnownedIsVictory()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(4500 - match.ElapsedTicks);

        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);
        Assert.All(match.Bases, b => Assert.Equal(match.HumanPlayer, b.Owner));

        // The sequence happens to sweep every base, but the rule under test doesn't depend on
        // that: re-derive the same guarantee from a match where one base is deliberately left
        // neutral, by evaluating the outcome directly once the AI alone is eliminated.
        var partial = new Match();
        var evaluateOutcome = typeof(Match).GetMethod("EvaluateOutcome", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ownerSetter = typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!;
        ownerSetter.Invoke(partial.Bases[1], new object?[] { null }); // the AI's only base becomes unowned
        ownerSetter.Invoke(partial.Bases[2], new object?[] { partial.HumanPlayer });
        ownerSetter.Invoke(partial.Bases[3], new object?[] { partial.HumanPlayer });
        // Bases 4 and 5 stay neutral - untouched by either player.
        evaluateOutcome.Invoke(partial, null);

        Assert.Equal(MatchOutcome.HumanVictory, partial.Outcome);
        Assert.Contains(partial.Bases, b => b.Owner is null);
    }

    [Fact]
    public void Advance_OnceDecided_IsANoOp_NotAnError()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(4500 - match.ElapsedTicks);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        var decidedTick = match.ElapsedTicks;
        var basesSnapshot = match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)).ToArray();

        match.Advance(500); // must be a no-op, not an exception

        Assert.Equal(decidedTick, match.ElapsedTicks);
        Assert.Equal(basesSnapshot, match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)));
    }

    [Fact]
    public void Execute_OnceDecided_RejectsWithDistinctReason_LeavingStateUntouched()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(4500 - match.ElapsedTicks);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        var anyBase = match.Bases[0];
        var garrisonBefore = anyBase.GarrisonCount;
        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, anyBase.Id, match.Bases[1].Id, 1));

        Assert.Equal(SendArmyOutcome.MatchAlreadyDecided, outcome);
        Assert.Equal(garrisonBefore, anyBase.GarrisonCount);
    }

    [Fact]
    public void MatchRunner_StopsConsultingTheBrain_OnceTheOutcomeIsDecided()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(4500 - match.ElapsedTicks);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        var decidedTick = match.ElapsedTicks;
        var snapshot = match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)).ToArray();
        var armiesSnapshot = match.ArmiesInFlight.Count;

        runner.Advance(5000); // if the runner still consulted the brain, this would issue commands

        Assert.Equal(decidedTick, match.ElapsedTicks);
        Assert.Equal(snapshot, match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)));
        Assert.Equal(armiesSnapshot, match.ArmiesInFlight.Count);
    }

    [Fact]
    public void Determinism_HoldsAcrossTheEnding_SingleCallVsIrregularChunks()
    {
        var oneCall = new Match();
        var oneCallRunner = new MatchRunner(oneCall, new AiBrain(oneCall.AiPlayer));
        SubmitWinningSequence(oneCall, oneCallRunner);
        oneCallRunner.Advance(4500 - oneCall.ElapsedTicks); // one call covers the whole remaining stretch, including the decision

        var chunked = new Match();
        var chunkedRunner = new MatchRunner(chunked, new AiBrain(chunked.AiPlayer));
        SubmitWinningSequence(chunked, chunkedRunner);
        foreach (var chunk in new long[] { 3, 17, 1, 40, 6, 123, 247, 63, 1900, 1690 })
        {
            chunkedRunner.Advance(chunk);
        }

        Assert.Equal(MatchOutcome.HumanVictory, oneCall.Outcome);
        Assert.Equal(oneCall.Outcome, chunked.Outcome);
        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)));
    }
}
