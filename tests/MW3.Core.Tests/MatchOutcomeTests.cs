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
    // Re-derived for #38: the tick rate halved (50ms) and the whole village ladder retuned to MW2's
    // published numbers (level-1 period 60 ticks, cap 20), so every count and timing tuned around
    // FR-3a's ladder no longer applied. The winning shape now sweeps the near flank first (bases 2
    // and 4), then the far flank (3 and 5), reinforcing the capital (base 1) along the way, and
    // finishes with two grinding strikes against the AI's own capped capital (base 2, formerly
    // AI-owned after a mid-game exchange) once nothing else on the board is winnable outright -
    // repeated waves whittle down a capped defender no single strike can beat alone.
    private static readonly (long Tick, int Source, int Target, int Count)[] _winningSequence =
    {
        (120, 0, 2, 6),
        (160, 0, 4, 3),
        (220, 0, 2, 2),
        (440, 0, 1, 3),
        (600, 2, 1, 3),
        (680, 0, 2, 3),
        (780, 0, 1, 3),
        (860, 0, 1, 2),
        (960, 2, 1, 3),
        (1080, 0, 2, 3),
        (1260, 1, 4, 3),
        (1380, 0, 4, 4),
        (1480, 1, 4, 3),
        (1600, 0, 4, 3),
        (1720, 1, 4, 3),
        (1800, 0, 4, 4),
        (1920, 1, 4, 4),
        (2280, 0, 3, 6),
        (2360, 1, 3, 5),
        (2440, 4, 5, 6),
        (2500, 0, 3, 4),
        (2500, 1, 2, 4),
        (2560, 4, 2, 4),
        (2580, 0, 5, 3),
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
    public void PassiveHuman_UpgradesOwnCapitalOnceThenIssuesNothingElse_StillReachesHumanDefeat_AndTheAiUpgradesToo()
    {
        // Re-authored for FR-6/issue #49 success criterion 5: growing its own economy once must not
        // hand the human a win against an AI that now upgrades its own bases too. The single human
        // upgrade is issued before anything else happens, then the human never acts again - the AI's
        // upgrade clause (D-31) must still let it out-produce and out-fight a human who did the one
        // "obviously good" thing and then stopped.
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);

        Assert.Equal(UpgradeOutcome.Accepted, runner.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        var aiEverUpgraded = false;

        for (var elapsed = 0L; elapsed < 5000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);

            if (match.Bases.Any(b => b.Owner == match.AiPlayer && b.Level > LevelTable.MinLevel))
            {
                aiEverUpgraded = true;
            }
        }

        Assert.Equal(MatchOutcome.HumanDefeat, match.Outcome);
        Assert.All(match.Bases, b => Assert.Equal(match.AiPlayer, b.Owner));
        Assert.True(aiEverUpgraded, "The AI never upgraded any base over the course of the match.");
    }

    [Fact]
    public void HandAuthoredHumanCommands_AgainstTheLiveAi_ReachHumanVictory()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        SubmitWinningSequence(match, runner);
        runner.Advance(3000 - match.ElapsedTicks);

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

        match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 10)); // arrives tick 76

        match.Advance(20);
        match.Execute(new SendArmyCommand(human, humanBase.Id, n4.Id, 7)); // arrives tick 20 + 59 = 79

        match.Advance(56); // elapsed 76: the AI captures the human's now-undefended capital
        Assert.Equal(ai, humanBase.Owner);
        Assert.Equal(MatchOutcome.InProgress, match.Outcome); // not eliminated - one army still in flight

        match.Advance(3); // elapsed 79: the human's own army lands and captures
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
        runner.Advance(3000 - match.ElapsedTicks);

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
        runner.Advance(3000 - match.ElapsedTicks);
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
        runner.Advance(3000 - match.ElapsedTicks);
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
        runner.Advance(3000 - match.ElapsedTicks);
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
        oneCallRunner.Advance(3000 - oneCall.ElapsedTicks); // one call covers the whole remaining stretch, including the decision

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
