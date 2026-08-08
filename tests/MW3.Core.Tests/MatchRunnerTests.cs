namespace MW3.Core.Tests;

public class MatchRunnerTests
{
    [Fact]
    public void Advance_HitsEveryDecisionTickExactlyOnce_RegardlessOfHowTicksAreChunked()
    {
        var oneCall = new Match();
        var oneCallRunner = new MatchRunner(oneCall, new AiBrain(oneCall.AiPlayer));
        oneCallRunner.Advance(437);

        var chunked = new Match();
        var chunkedRunner = new MatchRunner(chunked, new AiBrain(chunked.AiPlayer));
        foreach (var chunk in new long[] { 3, 17, 1, 40, 6, 123, 247 })
        {
            chunkedRunner.Advance(chunk);
        }

        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)),
            chunked.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)));
    }

    [Fact]
    public void Advance_ADecisionAtTickT_SeesStateAtTickT_AndLaunchesAtTickT()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        // The AI's first winnable move needs two level-1 production periods (120 ticks): with a
        // starting garrison of 10, floor(garrison/2) only exceeds a neutral's 5-unit garrison once
        // the AI base holds 12 - three decision ticks in, since nothing is won at 40 or 80.
        var decisionTick = 3 * MatchRunner.DecisionIntervalTicks;
        runner.Advance(decisionTick);

        var firstArmy = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(decisionTick, firstArmy.LaunchTick);
    }

    [Fact]
    public void Execute_SubmitsTheHumanCommandThroughTheSameMatchTheRunnerAdvances()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        var outcome = runner.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 3));

        Assert.Equal(SendArmyOutcome.Accepted, outcome);
        Assert.Equal(7, humanBase.GarrisonCount);
    }

    [Fact]
    public void Advance_NegativeTicks_ThrowsArgumentOutOfRangeException()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Advance(-1));
    }

    [Fact]
    public void AiBaseThatReachesItsCap_UpgradesOnTheNextDecisionTick_AndCompletesToLevelTwo()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        var cap = LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel)!.Value;
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(aiBase, new object?[] { cap });

        runner.Advance(MatchRunner.DecisionIntervalTicks); // first decision tick: the saturated base upgrades

        Assert.NotNull(aiBase.Construction);
        Assert.Equal(cap - LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), aiBase.GarrisonCount);
        Assert.Equal(LevelTable.MinLevel, aiBase.Level); // benefit still delayed (D-30)

        runner.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Null(aiBase.Construction);
        Assert.Equal(LevelTable.MinLevel + 1, aiBase.Level);
        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel + 1), aiBase.GarrisonCap);
    }

    /// <summary>
    /// Re-authored for FR-7 (issue #53): <c>TryConvert</c>'s candidate rule is a flat garrison
    /// threshold (D-31's own text, "garrison at least LevelTable.ConversionCost"), not a cap-relative
    /// one the way <c>TryUpgrade</c>'s is - so a base that picks up a large reinforcement stack well
    /// under its level's cap can legitimately be converted to a tower instead of continuing to
    /// upgrade, cutting its climb toward level 3 short. That is new, sanctioned AI behavior, not a
    /// regression: the original expectation (some base reaches level 3) is replaced with the wider
    /// property this phase actually promises - the AI meaningfully spends a saturated base's surplus
    /// one way or the other, exactly as FR-6's and FR-7's own doc comments describe upgrading and
    /// converting as the two forms of the same self-investment decision.
    /// </summary>
    [Fact]
    public void AiInvestsItsSurplus_ReachingLevelThreeOrBuildingATower_OverALongMatch()
    {
        // The phase-6 shipped board's extra forge/tower slots (fixture) give the AI enough
        // opportunity to reach this outcome within the tick budget; FR-2's default (MapCatalog.Small,
        // six bases) does not reliably, and the running application never uses the default anyway.
        var match = new Match(PhaseSixEightSlotFixture.Slots);
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        for (var elapsed = 0L; elapsed < 20_000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);

            var reachedLevelThree = match.Bases.Any(b => b.Owner == match.AiPlayer && b.Level >= 3);
            var builtATower = match.Bases.Any(b => b.Owner == match.AiPlayer && b.Type == BaseType.Tower);
            if (reachedLevelThree || builtATower)
            {
                return;
            }
        }

        Assert.Fail("The AI neither reached level 3 on any base nor built a tower within the budget.");
    }

    [Fact]
    public void PassiveHuman_AiCapturesEveryBase_WithinFiveThousandTickBudget()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        var reachedFullOwnership = false;
        for (var elapsed = 0L; elapsed < 5000; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);
            if (match.Bases.All(b => b.Owner == match.AiPlayer))
            {
                reachedFullOwnership = true;
                break;
            }
        }

        Assert.True(reachedFullOwnership, "AI did not capture every base within the 5000-tick budget.");
        Assert.All(match.Bases, b => Assert.Equal(match.AiPlayer, b.Owner));
    }

    [Fact]
    public void PassiveHuman_AiNeverIdleLocksForTwoHundredConsecutiveTicks()
    {
        // Once the AI owns every base there is nothing left to defend, attack, or consolidate -
        // that is the match's natural end in a passive-human game, not an idle lock, so the check
        // only needs to hold up to that point (mirrors the "captures every base" test's cutoff).
        var match = new Match();
        var brain = new AiBrain(match.AiPlayer);

        var lastCommandTick = 0L;
        for (var tick = MatchRunner.DecisionIntervalTicks; tick <= 5000; tick += MatchRunner.DecisionIntervalTicks)
        {
            match.Advance(tick - match.ElapsedTicks);

            if (match.Bases.All(b => b.Owner == match.AiPlayer))
            {
                return;
            }

            var decision = brain.Decide(match);

            var ownsMultipleBases = match.Bases.Count(b => b.Owner == match.AiPlayer) >= 2;
            var hasGrowableBase = match.Bases.Any(b => b.Owner == match.AiPlayer && b.GarrisonCount >= 2);

            if (decision.HasCommand)
            {
                if (decision.IsUpgrade)
                {
                    match.Execute(decision.Upgrade);
                }
                else if (decision.IsConvert)
                {
                    match.Execute(decision.Convert);
                }
                else
                {
                    match.Execute(decision.Command);
                }

                lastCommandTick = tick;
            }
            else if (ownsMultipleBases && hasGrowableBase)
            {
                Assert.True(tick - lastCommandTick < 200, $"AI idle-locked for 200+ ticks at tick {tick}.");
            }
        }

        Assert.Fail("AI never reached full ownership within the 5000-tick budget.");
    }

    [Fact]
    public void PassiveHuman_NoArmyInFlight_IsEverOwnedByTheHumanPlayer()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        for (var elapsed = 0L; elapsed < 2000; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);
            Assert.All(match.ArmiesInFlight, a => Assert.Equal(match.AiPlayer, a.Owner));
        }
    }
}
