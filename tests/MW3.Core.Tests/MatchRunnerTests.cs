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

    [Fact]
    public void AiLaddersPastLevelTwo_ReachingLevelThreeOnAtLeastOneBase_OverALongMatch()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));

        for (var elapsed = 0L; elapsed < 20_000 && match.Outcome == MatchOutcome.InProgress; elapsed += MatchRunner.DecisionIntervalTicks)
        {
            runner.Advance(MatchRunner.DecisionIntervalTicks);

            if (match.Bases.Any(b => b.Owner == match.AiPlayer && b.Level >= 3))
            {
                return;
            }
        }

        Assert.Fail("No AI base reached level 3 within the budget.");
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
