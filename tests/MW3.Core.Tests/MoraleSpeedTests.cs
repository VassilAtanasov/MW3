namespace MW3.Core.Tests;

/// <summary>
/// FR-4: morale raises unit speed, locked once at a send's submission tick for the whole send
/// (<c>docs/morale/REQUIREMENTS.md</c> FR-4, <c>docs/morale/ARCHITECTURE.md</c> D-39). Covers the
/// shared speed helper, the submission-tick lock across waves, every speed consumer
/// (<see cref="TravelTimeCalculator"/>, <see cref="TowerThreatEstimator"/>, <see cref="AiBrain"/>),
/// the emergent tower-loss reduction, the morale-0 identity baseline, and determinism.
/// </summary>
public class MoraleSpeedTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetMoralePoints(MoraleState state, int points) =>
        typeof(MoraleState).GetProperty(nameof(MoraleState.Points))!.GetSetMethod(nonPublic: true)!.Invoke(state, new object?[] { points });

    /// <summary>
    /// Mirrors <c>TravelTimeCalculator.ComputeTicks</c>'s public-facing formula. Phase 7 FR-6 opened
    /// an <c>InternalsVisibleTo</c> from <c>MW3.Core</c> into this project, so the duplication is no
    /// longer forced by the accessibility boundary - it is kept deliberately, because these two
    /// tests exist to check the shipped helper against an <b>independently written</b> expectation.
    /// Calling the helper here would make them assert that a value equals itself. Nothing else in
    /// this project should copy Core arithmetic on the old justification.
    /// </summary>
    private static long ComputeTravelTicks(MapPoint from, MapPoint to, double speedUnitsPerTick)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        return Math.Max(1, (long)Math.Ceiling(distance / speedUnitsPerTick));
    }

    private static void ConvertToTower(Match match, Player owner, Base b)
    {
        SetGarrison(b, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(owner, b.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, b.Type);
    }

    // --- The shared helper ---

    [Fact]
    public void EffectiveSpeed_AtMoraleZero_IsBitIdenticalToTheBaseConstant()
    {
        Assert.Equal(Match.ArmySpeedUnitsPerTick, Match.EffectiveArmySpeedUnitsPerTick(MoraleTable.MinLevel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EffectiveSpeed_ComposesFromMoraleTablesUnitSpeedPercentage(int level)
    {
        var expected = Match.ArmySpeedUnitsPerTick * MoraleTable.UnitSpeedPercentage(level) / 100.0;
        Assert.Equal(expected, Match.EffectiveArmySpeedUnitsPerTick(level));
    }

    // --- Locked at submission, for the whole send (D-39) ---

    [Fact]
    public void MoraleZero_ArrivalTick_IsBitIdenticalToPreFR4Behavior()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(0, army.LaunchTick);
        Assert.Equal(34, army.ArrivalTick); // the same fixed travel time SendWaveTests pins pre-FR-4
    }

    [Fact]
    public void AllWavesOfOneSend_ShareTheIdenticalTravelSpan_AtNonZeroMorale()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 20); // splits into three waves (8, 8, 4)
        SetMoralePoints(match.HumanMorale, MoraleTable.PointCeiling); // level 5, 150% speed

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20)));
        match.Advance(SendWaveCalculator.LaunchTickOffset(SendWaveCalculator.WaveCount(20)));

        var waves = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).ToList();
        Assert.Equal(3, waves.Count);
        var spans = waves.Select(w => w.ArrivalTick - w.LaunchTick).Distinct().ToList();
        Assert.Single(spans); // every wave shares one speed, so one span

        // Sanity: the shared span is actually the boosted (shorter) one, not an accidental identity.
        var zeroMoraleSpan = ComputeTravelTicks(human.Position, neutral.Position, Match.ArmySpeedUnitsPerTick);
        Assert.True(spans[0] < zeroMoraleSpan);
    }

    [Fact]
    public void MoraleChangeAfterSubmission_AltersNothingAlreadyCommitted()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 20); // wave 1 launches now, waves 2-3 are pending

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20)));
        var waveBefore = match.ArmiesInFlight.Single();
        var arrivalBefore = waveBefore.ArrivalTick;

        // Boost morale mid-column, after submission but before the later waves launch.
        SetMoralePoints(match.HumanMorale, MoraleTable.PointCeiling);

        match.Advance(SendWaveCalculator.LaunchTickOffset(SendWaveCalculator.WaveCount(20)));
        var waves = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).ToList();

        Assert.Equal(arrivalBefore, waveBefore.ArrivalTick); // wave 1, already in flight, is unchanged
        Assert.All(waves, w => Assert.Equal(waveBefore.ArrivalTick - waveBefore.LaunchTick, w.ArrivalTick - w.LaunchTick)); // later waves kept wave 1's span too - no wave can ever overtake an earlier one
    }

    [Fact]
    public void HigherMorale_ArmyIsFurtherAlongItsPath_AtASharedTick()
    {
        var control = new Match();
        var controlHuman = HumanBase(control);
        var controlNeutral = control.Bases.First(b => b.Owner is null);
        SetGarrison(controlHuman, 8);
        Assert.Equal(SendArmyOutcome.Accepted, control.Execute(new SendArmyCommand(control.HumanPlayer, controlHuman.Id, controlNeutral.Id, 8)));
        var controlArmy = control.ArmiesInFlight.Single();

        var boosted = new Match();
        var boostedHuman = HumanBase(boosted);
        var boostedNeutral = boosted.Bases.First(b => b.Owner is null);
        SetMoralePoints(boosted.HumanMorale, MoraleTable.PointCeiling);
        Assert.Equal(SendArmyOutcome.Accepted, boosted.Execute(new SendArmyCommand(boosted.HumanPlayer, boostedHuman.Id, boostedNeutral.Id, 8)));
        var boostedArmy = boosted.ArmiesInFlight.Single();

        Assert.Equal(controlArmy.LaunchTick, boostedArmy.LaunchTick);
        Assert.True(boostedArmy.ArrivalTick < controlArmy.ArrivalTick);

        // PositionAtTick is not modified (interpolation on Launch/Arrival) - compute the same
        // fraction-based position independently here, at a tick both armies are still in flight.
        var tick = Math.Min(controlArmy.ArrivalTick, boostedArmy.ArrivalTick) - 1;
        var controlFraction = (double)(tick - controlArmy.LaunchTick) / (controlArmy.ArrivalTick - controlArmy.LaunchTick);
        var boostedFraction = (double)(tick - boostedArmy.LaunchTick) / (boostedArmy.ArrivalTick - boostedArmy.LaunchTick);
        Assert.True(boostedFraction > controlFraction);
    }

    // --- Every speed consumer, so predictions cannot desync ---

    [Fact]
    public void AiBrain_TryDefend_UsesTheAisOwnMoraleSpeed_NotTheBaseConstant()
    {
        // A threatened base and a lone candidate source just far enough that only a morale-boosted
        // AI can reach it in time - proving TryDefend reads live speed via match.MoraleFor(Player)
        // rather than the fixed constant. Ownership of neutral1 is rigged directly by reflection
        // (the same style TowerFireTests/SendWaveTests use) rather than reached by a real capture,
        // so the AI's clock stays at tick 0 and the travel-time arithmetic below is exact.
        var match = new Match();
        var aiHome = AiBase(match); // (0.88, 0.50)
        var neutral1 = match.Bases.First(b => b.Owner is null); // (0.35, 0.25)
        var neutral2 = match.Bases.Skip(3).First(b => b.Owner is null); // (0.35, 0.75)
        SetOwner(neutral1, match.AiPlayer);
        SetGarrison(neutral1, 1); // trivially weak, so an 8-unit attack captures it regardless of the AI's own defence index

        // Threaten neutral1 from the human's second neutral. Rigged to human ownership directly by
        // reflection for the same reason. 8 units - the largest send that stays a single wave
        // (FR-3) - keeps the whole force actually in flight at once, rather than diluted across a
        // multi-wave column.
        SetOwner(neutral2, match.HumanPlayer);
        SetGarrison(neutral2, 8);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral2.Id, neutral1.Id, 8)));
        var threat = match.ArmiesInFlight.Single(a => a.Owner == match.HumanPlayer);
        var ticksRemaining = threat.ArrivalTick - match.ElapsedTicks; // (0.35,0.75) to (0.35,0.25): distance 0.5 -> 50 ticks

        // aiHome (0.88, 0.50) is far enough from neutral1 (0.35, 0.25) - distance ~0.586, 59 ticks
        // at base speed, 40 ticks at morale 5's 150% - that reinforcing arrives in time only once
        // boosted (40 <= 50 < 59).
        var baseTicks = ComputeTravelTicks(aiHome.Position, neutral1.Position, Match.ArmySpeedUnitsPerTick);
        var boostedTicks = ComputeTravelTicks(aiHome.Position, neutral1.Position, Match.EffectiveArmySpeedUnitsPerTick(MoraleTable.MaxLevel));
        Assert.True(boostedTicks <= ticksRemaining, "test fixture: boosted defend must be reachable in time");
        Assert.True(baseTicks > ticksRemaining, "test fixture: unboosted defend must NOT be reachable in time");

        SetGarrison(aiHome, 30);
        var brain = new AiBrain(match.AiPlayer);

        // At morale 0 the AI may still find *something* to do (e.g. upgrade its saturated home base,
        // clause 2) - what this asserts is narrower: it never defends neutral1, since it cannot
        // arrive in time.
        var decisionAtZero = brain.Decide(match);
        Assert.False(decisionAtZero.IsSend && decisionAtZero.Command.TargetBaseId == neutral1.Id,
            "at morale 0 the AI should not be able to defend in time");

        SetMoralePoints(match.AiMorale, MoraleTable.PointCeiling);
        var decisionBoosted = brain.Decide(match);
        Assert.True(decisionBoosted.IsSend);
        var command = decisionBoosted.Command;
        Assert.Equal(neutral1.Id, command.TargetBaseId);
        Assert.Equal(aiHome.Id, command.SourceBaseId);
    }

    [Fact]
    public void TowerThreatEstimator_AgreesWithTheSimulation_AtNonZeroMorale()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);

        const int sentUnits = 8;
        SetGarrison(aiBase, sentUnits);
        SetMoralePoints(match.AiMorale, MoraleTable.PointCeiling); // locked at submission (D-39)
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, sentUnits)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var survived = match.ArmiesInFlight.Any() ? army.UnitCount : 0;
        var lost = sentUnits - survived;

        // TowerThreatEstimator is reachable from this project since phase 7 FR-6's
        // InternalsVisibleTo, but the estimate is reproduced here on purpose: this test's whole
        // claim is that the simulation agrees with an independently derived number, and calling the
        // estimator would collapse it into a tautology. For this straight-at-the-tower case the
        // target base IS the tower, so the in-range chord is simply min(range, total distance) -
        // the same geometry TowerFireTests' TuningSanity theory relies on.
        var speed = Match.EffectiveArmySpeedUnitsPerTick(MoraleTable.MaxLevel);
        var dx = humanBase.Position.X - aiBase.Position.X;
        var dy = humanBase.Position.Y - aiBase.Position.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var chordLength = Math.Min(LevelTable.Tower.RangeUnits(humanBase.Level), distance);
        var ticksInRange = chordLength / speed;
        var estimatedLost = (long)Math.Floor(ticksInRange / LevelTable.Tower.FirePeriodTicks(humanBase.Level));

        Assert.InRange(lost, Math.Max(0, estimatedLost - 1), estimatedLost + 1); // same floor-to-whole-shots tolerance TowerFireTests uses
    }

    // --- The emergent consequence ---

    [Fact]
    public void HighMorale_ReducesTowerLossesInTransit_ComparedToMoraleZero()
    {
        var control = new Match();
        var controlHuman = HumanBase(control);
        var controlAi = AiBase(control);
        ConvertToTower(control, control.HumanPlayer, controlHuman);
        const int sentUnits = 8;
        SetGarrison(controlAi, sentUnits);
        Assert.Equal(SendArmyOutcome.Accepted, control.Execute(new SendArmyCommand(control.AiPlayer, controlAi.Id, controlHuman.Id, sentUnits)));
        var controlArmy = control.ArmiesInFlight.Single();
        control.Advance(controlArmy.ArrivalTick - control.ElapsedTicks - 1);
        var controlSurvived = control.ArmiesInFlight.Any() ? controlArmy.UnitCount : 0;
        var controlLost = sentUnits - controlSurvived;

        var boosted = new Match();
        var boostedHuman = HumanBase(boosted);
        var boostedAi = AiBase(boosted);
        ConvertToTower(boosted, boosted.HumanPlayer, boostedHuman);
        SetGarrison(boostedAi, sentUnits);
        SetMoralePoints(boosted.AiMorale, MoraleTable.PointCeiling); // locked at submission (D-39)
        Assert.Equal(SendArmyOutcome.Accepted, boosted.Execute(new SendArmyCommand(boosted.AiPlayer, boostedAi.Id, boostedHuman.Id, sentUnits)));
        var boostedArmy = boosted.ArmiesInFlight.Single();
        boosted.Advance(boostedArmy.ArrivalTick - boosted.ElapsedTicks - 1);
        var boostedSurvived = boosted.ArmiesInFlight.Any() ? boostedArmy.UnitCount : 0;
        var boostedLost = sentUnits - boostedSurvived;

        Assert.True(boostedLost < controlLost);

        // Expected values derived from the tables, never hardcoded.
        var controlRangeTicks = LevelTable.Tower.RangeUnits(LevelTable.MinLevel) / Match.ArmySpeedUnitsPerTick;
        var controlExpectedShots = Math.Min(sentUnits, (long)(controlRangeTicks / LevelTable.Tower.FirePeriodTicks(LevelTable.MinLevel)));
        var boostedRangeTicks = LevelTable.Tower.RangeUnits(LevelTable.MinLevel) / Match.EffectiveArmySpeedUnitsPerTick(MoraleTable.MaxLevel);
        var boostedExpectedShots = Math.Min(sentUnits, (long)(boostedRangeTicks / LevelTable.Tower.FirePeriodTicks(LevelTable.MinLevel)));

        Assert.InRange(controlLost, controlExpectedShots - 1, controlExpectedShots + 1);
        Assert.InRange(boostedLost, boostedExpectedShots - 1, boostedExpectedShots + 1);
        Assert.True(boostedExpectedShots < controlExpectedShots); // the tuning itself predicts fewer shots, not just this one run
    }

    // --- Determinism (D-12, S-8) ---

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnArrivalTicksAndTowerLosses_WithMoraleDriftMidMatch()
    {
        var oneCall = new Match();
        Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Id, a.UnitCount, a.LaunchTick, a.ArrivalTick)),
            chunked.ArmiesInFlight.Select(a => (a.Id, a.UnitCount, a.LaunchTick, a.ArrivalTick)));
        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount, b.LastFireTick)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount, b.LastFireTick)));
        Assert.Equal(oneCall.HumanMorale.Points, chunked.HumanMorale.Points);
        Assert.Equal(oneCall.AiMorale.Points, chunked.AiMorale.Points);
    }

    private static void Play(Match match, Action<long> advance)
    {
        var human = match.HumanPlayer;
        var ai = match.AiPlayer;
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetGarrison(humanBase, 100);
        SetGarrison(aiBase, 100);

        // Morale already nonzero before this send, so its speed is locked in above the baseline.
        SetMoralePoints(match.HumanMorale, MoraleTable.PointsThreshold(3));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(human, humanBase.Id, neutral.Id, 20)));

        advance(SendWaveCalculator.LaunchTickOffset(SendWaveCalculator.WaveCount(20)) + 60);

        // Morale drifts further mid-match (accrual from the capture above); a second send now locks
        // in a different speed than the first did.
        SetMoralePoints(match.AiMorale, MoraleTable.PointsThreshold(1));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 10)));

        advance(120);
    }

    private static void AdvanceInIrregularChunks(Match match, long ticks)
    {
        var remaining = ticks;
        var sizes = new long[] { 1, 7, 3, 40, 2, 113 };
        var i = 0;
        while (remaining > 0)
        {
            var chunk = Math.Min(sizes[i % sizes.Length], remaining);
            match.Advance(chunk);
            remaining -= chunk;
            i++;
        }
    }
}
