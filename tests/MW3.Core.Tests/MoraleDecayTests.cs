namespace MW3.Core.Tests;

/// <summary>
/// FR-3: inactivity decay (<c>docs/morale/REQUIREMENTS.md</c> FR-3, <c>docs/morale/ARCHITECTURE.md</c>
/// D-38). Covers the schedule, the self-slowing rate, what does and does not reset the timer, the
/// freeze, and determinism - the acceptance criteria on issue #69.
/// </summary>
public class MoraleDecayTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetMoralePoints(MoraleState state, int points) =>
        typeof(MoraleState).GetProperty(nameof(MoraleState.Points))!.GetSetMethod(nonPublic: true)!.Invoke(state, new object?[] { points });

    /// <summary>
    /// Mirrors <c>Match</c>'s decay algorithm exactly (re-reading the level from <see cref="MoraleTable"/>
    /// every period), so expected values in these tests are derived rather than hardcoded, per the
    /// issue's own instruction. Assumes the player's <c>LastSendTick</c> is 0 (never sent) and
    /// <paramref name="durationTicks"/> ticks have elapsed since.
    /// </summary>
    private static int SimulateDecay(int startPoints, long durationTicks)
    {
        var points = startPoints;
        for (var t = MoraleTable.DecayPeriodTicks; t <= durationTicks; t += MoraleTable.DecayPeriodTicks)
        {
            var level = MoraleTable.LevelForPoints(points);
            if (t >= MoraleTable.DecayThresholdTicks(level))
            {
                points = MoraleTable.ClampPoints(points - MoraleTable.DecayPointsPerPeriod(level));
            }
        }

        return points;
    }

    // ---- The schedule ----

    [Theory]
    [InlineData(0, 300)]
    [InlineData(1, 700)]
    [InlineData(2, 1500)]
    [InlineData(3, 3000)]
    [InlineData(4, 6000)]
    [InlineData(5, 8000)]
    public void FirstDecay_LandsExactlyOnTheThresholdTick_NotBeforeNotAfter(int level, int startPoints)
    {
        Assert.Equal(level, MoraleTable.LevelForPoints(startPoints)); // sanity: the fixture is actually at this level

        var match = new Match();
        SetMoralePoints(match.HumanMorale, startPoints);

        var threshold = MoraleTable.DecayThresholdTicks(level);

        match.Advance(threshold - 1);
        Assert.Equal(startPoints, match.HumanMorale.Points); // one tick early: untouched

        match.Advance(1);
        Assert.Equal(startPoints - MoraleTable.DecayPointsPerPeriod(level), match.HumanMorale.Points); // exactly on the threshold tick
    }

    [Fact]
    public void PointsFloorAtZero_ALargerDecayThanRemainingPointsNeverGoesNegative()
    {
        var match = new Match();
        SetMoralePoints(match.HumanMorale, 5); // far less than the level-0 rate of 10

        match.Advance(MoraleTable.DecayThresholdTicks(0));

        Assert.Equal(0, match.HumanMorale.Points);
    }

    [Fact]
    public void APlayerAtZeroPoints_DecaysToNoEffect()
    {
        var match = new Match();

        match.Advance(1000); // well past every level's threshold

        Assert.Equal(0, match.HumanMorale.Points);
        Assert.Equal(0, match.AiMorale.Points);
    }

    // ---- No new mutable state / never stalls ----

    [Fact]
    public void ChunkedAndSingleAdvance_ThroughALongIdlePeriod_AgreeExactly()
    {
        var single = new Match();
        SetMoralePoints(single.HumanMorale, MoraleTable.PointCeiling);
        single.Advance(1200);

        var chunked = new Match();
        SetMoralePoints(chunked.HumanMorale, MoraleTable.PointCeiling);
        var sizes = new long[] { 1, 3, 7, 40, 2, 113, 1, 1, 250 };
        var remaining = 1200L;
        var i = 0;
        while (remaining > 0)
        {
            var chunk = Math.Min(sizes[i % sizes.Length], remaining);
            chunked.Advance(chunk);
            remaining -= chunk;
            i++;
        }

        Assert.Equal(single.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(single.HumanMorale.Points, chunked.HumanMorale.Points);
        Assert.Equal(single.HumanMorale.Level, chunked.HumanMorale.Level);
        Assert.NotEqual(MoraleTable.PointCeiling, single.HumanMorale.Points); // sanity: decay actually ran
    }

    /// <summary>
    /// Thresholds lengthen as morale falls (100 ticks at level 5 up to 200 at level 0), so a naive
    /// reading suggests a decay run could pause when a level drop raises the threshold above the
    /// idle time already accumulated. It cannot: idle time grows faster than the threshold does,
    /// since the idle clock never resets while the threshold only ever grows by at most 20 ticks per
    /// level dropped. Walks a full run from the ceiling to zero and asserts every consecutive
    /// 20-tick period decayed, with no gap.
    /// </summary>
    [Fact]
    public void DecaySchedule_NeverStalls_WalkingTheFullRangeFromCeilingToZero()
    {
        var match = new Match();
        SetMoralePoints(match.HumanMorale, MoraleTable.PointCeiling);

        var previous = MoraleTable.PointCeiling;
        long tick = 0;
        while (match.HumanMorale.Points > 0 && tick < 5000) // 5000 is a generous ceiling far beyond any real run
        {
            match.Advance(MoraleTable.DecayPeriodTicks);
            tick += MoraleTable.DecayPeriodTicks;

            var expected = SimulateDecay(MoraleTable.PointCeiling, tick);
            Assert.Equal(expected, match.HumanMorale.Points);

            if (tick > MoraleTable.DecayThresholdTicks(0))
            {
                // Past the level-0 threshold, decay never stalls: every period decays something once
                // it has started, so points strictly decrease every period until the floor.
                Assert.True(match.HumanMorale.Points < previous || match.HumanMorale.Points == 0);
            }

            previous = match.HumanMorale.Points;
        }

        Assert.Equal(0, match.HumanMorale.Points);
    }

    // ---- Self-slowing ----

    /// <summary>
    /// The same idle duration costs strictly more the higher a player started, because a higher
    /// level's threshold is shorter and its rate is larger. Expected values are the full simulation
    /// (<see cref="SimulateDecay"/>, itself built only from <see cref="MoraleTable"/>), not a
    /// hand-picked number, since level 5 self-slows to level 4 after its own first period and the
    /// naive "periods * rate" arithmetic would be wrong for it.
    /// </summary>
    [Fact]
    public void TheSameIdleDuration_CostsMoreTheHigherTheStartingLevel()
    {
        const long duration = 200;

        var level5 = new Match();
        SetMoralePoints(level5.HumanMorale, MoraleTable.PointCeiling);
        level5.Advance(duration);

        var level3 = new Match();
        SetMoralePoints(level3.HumanMorale, MoraleTable.PointsThreshold(3));
        level3.Advance(duration);

        var level1 = new Match();
        SetMoralePoints(level1.HumanMorale, MoraleTable.PointsThreshold(1));
        level1.Advance(duration);

        Assert.Equal(SimulateDecay(MoraleTable.PointCeiling, duration), level5.HumanMorale.Points);
        Assert.Equal(SimulateDecay(MoraleTable.PointsThreshold(3), duration), level3.HumanMorale.Points);
        Assert.Equal(SimulateDecay(MoraleTable.PointsThreshold(1), duration), level1.HumanMorale.Points);

        var lost5 = MoraleTable.PointCeiling - level5.HumanMorale.Points;
        var lost3 = MoraleTable.PointsThreshold(3) - level3.HumanMorale.Points;
        var lost1 = MoraleTable.PointsThreshold(1) - level1.HumanMorale.Points;

        Assert.True(lost5 > lost3, $"expected level-5 loss ({lost5}) to exceed level-3 loss ({lost3})");
        Assert.True(lost3 > lost1, $"expected level-3 loss ({lost3}) to exceed level-1 loss ({lost1})");
    }

    /// <summary>
    /// The reference's own worked example (<c>MW2-RULES.md</c> §5.4, <c>[D]</c>) and D-38's
    /// reconciliation of it: from 8 000 points, the first decay period drops the player out of level
    /// 5 immediately, and the rate falls to level 4's -100 per second. D-38 reads the note's "about
    /// 40 seconds" as the near-continuous distance from there down to level 4's own threshold
    /// (4 000 points) - the boundary crossed to reach level 3 - at that constant rate:
    /// 4 000 / 100 = 40s (800 ticks), the first period's negligible 200-point head start folded in as
    /// the source of "about" rather than exact.
    /// </summary>
    [Fact]
    public void WorkedExample_FromTheCeiling_FirstPeriodDropsOutOfLevel5_ThenAbout40SecondsToLevel3()
    {
        var match = new Match();
        SetMoralePoints(match.HumanMorale, MoraleTable.PointCeiling);

        match.Advance(MoraleTable.DecayThresholdTicks(5)); // 100 ticks: the first decay period

        Assert.Equal(MoraleTable.PointCeiling - MoraleTable.DecayPointsPerPeriod(5), match.HumanMorale.Points);
        Assert.Equal(4, match.HumanMorale.Level); // dropped out of level 5 immediately

        // Walk forward in 20-tick periods until the player actually enters level 3 (crosses below
        // the level-4 threshold of 4 000 points), and confirm the total elapsed time since idling
        // began is close to 800 ticks (40s) - the published note's approximation.
        while (match.HumanMorale.Level >= 4)
        {
            match.Advance(MoraleTable.DecayPeriodTicks);
        }

        Assert.Equal(3, match.HumanMorale.Level);
        Assert.InRange(match.ElapsedTicks, 700, 900); // "about 40 seconds" (800 ticks), slack for the approximation
    }

    // ---- What resets the timer ----

    [Fact]
    public void MatchStart_LastSendTickIsTreatedAsTheStartTick_APlayerWhoNeverSendsIsIdleFromTheBeginning()
    {
        var match = new Match();
        SetMoralePoints(match.HumanMorale, 300); // level 0, headroom above the level-0 rate

        Assert.Null(match.HumanMorale.LastSendTick); // FR-1's representation: never written, not defaulted to 0

        match.Advance(MoraleTable.DecayThresholdTicks(0));

        Assert.Equal(300 - MoraleTable.DecayPointsPerPeriod(0), match.HumanMorale.Points); // decayed anyway
    }

    [Fact]
    public void AnAcceptedUpgrade_DoesNotResetTheTimer()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);

        match.Advance(37);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        Assert.Null(match.HumanMorale.LastSendTick); // still never sent
    }

    [Fact]
    public void AnAcceptedConvert_DoesNotResetTheTimer()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        SetGarrison(humanBase, LevelTable.ConversionCost + 5);

        match.Advance(37);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));

        Assert.Null(match.HumanMorale.LastSendTick);
    }

    /// <summary>
    /// A defender whose tower is killing attackers gains +10 per kill and still decays: kills,
    /// captures, and completed upgrades all leave <c>LastSendTick</c> alone. Pinned exactly, per the
    /// issue's warning that resetting on any morale event "guts the anti-turtle rule precisely where
    /// turtling is most attractive."
    /// </summary>
    [Fact]
    public void GainingMoraleFromTowerKills_DoesNotResetTheTimer_StillDecaysOnSchedule()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(humanBase, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);

        // The AI sends into the human's tower range and is destroyed outright - Human gains, but
        // never sends (mirrors MoraleAccrualTests.TowerFire_DestroyingAnArmyOutright...).
        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks + 5); // well past arrival - the tower gets every shot

        Assert.Null(match.HumanMorale.LastSendTick); // Human never sent - only converted and defended
        var pointsAfterKills = match.HumanMorale.Points;
        Assert.Equal(4 * MoraleTable.AttackingUnitDestroyedGain, pointsAfterKills); // sanity: the gain actually landed

        // Idle from here with no further send. Had the timer reset on the tower's own kills, decay
        // would never see 200 idle ticks accrued since the (never-happened) send; instead it is
        // measured against LastSendTick's implicit match-start value (0) the whole time.
        var idleTicksNeeded = MoraleTable.DecayThresholdTicks(0) - (int)match.ElapsedTicks;
        Assert.True(idleTicksNeeded > 0, "the scenario should reach this point before the level-0 decay threshold");
        match.Advance(idleTicksNeeded);

        Assert.Equal(pointsAfterKills - MoraleTable.DecayPointsPerPeriod(0), match.HumanMorale.Points);
    }

    /// <summary>The AI's sends reset the AI's timer on identical terms to the human - nothing branches on <see cref="PlayerControllerKind"/>.</summary>
    [Fact]
    public void AiSend_ResetsTheAisTimer_OnIdenticalTermsToTheHuman()
    {
        var match = new Match();
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        match.Advance(53);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, neutral.Id, 3)));

        Assert.Equal(53, match.AiMorale.LastSendTick);
        Assert.Null(match.HumanMorale.LastSendTick);
    }

    // ---- Boundaries and the freeze ----

    [Fact]
    public void NothingDecays_OnceOutcomeLeavesInProgress()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetMoralePoints(match.HumanMorale, 300);

        // Eliminate the AI: the human sends everything at the AI's base with the AI holding nothing
        // to counter it, deciding the match well before the level-0 decay threshold of 200 ticks.
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 50);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 20)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);
        var pointsAtDecision = match.HumanMorale.Points;

        match.Advance(1000); // long past the decay threshold, but the match is already decided

        Assert.Equal(pointsAtDecision, match.HumanMorale.Points); // frozen (phase 2 FR-7)
    }

    /// <summary>
    /// Decay is evaluated after tower fire and arrivals: a wave landing on the same tick a decay
    /// period is due has already scored, and decay applies to the post-combat total, not the
    /// pre-combat one.
    /// </summary>
    [Fact]
    public void DecayAppliesToThePostCombatTotal_WhenAnArrivalLandsOnTheSameDecayBoundary()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetMoralePoints(match.HumanMorale, 300); // level 0, headroom
        SetGarrison(neutral, 1); // trivial capture, minimal death swing

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 2)));
        var army = match.ArmiesInFlight.Single();
        var arrivalTick = army.ArrivalTick;

        match.Advance(arrivalTick - match.ElapsedTicks); // resolve the capture first
        var pointsAfterCapture = match.HumanMorale.Points;

        // The send was issued at tick 0 (before any Advance call), so LastSendTick is 0 - walk the
        // remaining ticks to land exactly on the level-0 decay threshold.
        var remainingToThreshold = MoraleTable.DecayThresholdTicks(0) - (int)arrivalTick;
        Assert.True(remainingToThreshold > 0, "the capture should resolve before the level-0 decay threshold");
        match.Advance(remainingToThreshold);

        Assert.Equal(pointsAfterCapture - MoraleTable.DecayPointsPerPeriod(0), match.HumanMorale.Points);
    }
}
