using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// FR-3: a send of more than <see cref="SendWaveCalculator.WaveSizeUnits"/> units splits into
/// successive waves that launch, travel, and resolve independently (D-33, D-35). Covers the pieces
/// <see cref="SendArmyTests"/>, <see cref="TowerFireTests"/>, <see cref="CombatTests"/>, and
/// <see cref="CaptureDemotionTests"/> do not: splitting itself, pending-launch visibility, wave
/// metadata, and the deliberately-weaker-than-a-single-arrival combat comparison.
/// </summary>
public class SendWaveTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetOwnerBeforeLastChange(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.OwnerBeforeLastChange))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetLastOwnerChangeTick(Base b, long? tick) =>
        typeof(Base).GetProperty(nameof(Base.LastOwnerChangeTick))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { tick });

    /// <summary>
    /// Rigging <see cref="Base.GarrisonCount"/> directly does not touch banked production progress
    /// from ticks before the rig - zero it too, so a rigged base's growth over a subsequent Advance
    /// is exactly what its period predicts, not inflated by progress accumulated under an earlier,
    /// unrelated level or garrison.
    /// </summary>
    private static void SetGarrisonAndResetProduction(Base b, int garrison)
    {
        SetGarrison(b, garrison);
        typeof(Base).GetProperty(nameof(Base.ProductionProgressTicks))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { 0L });
    }

    // --- Splitting ---

    [Fact]
    public void Send_Above8Units_SplitsIntoFullWavesPlusRemainder_InOrder()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 20);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20)));
        match.Advance(SendWaveCalculator.LaunchTickOffset(SendWaveCalculator.WaveCount(20))); // past every wave's own launch tick, before any arrival

        var waves = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).ToList();
        Assert.Equal(new[] { 8, 8, 4 }, waves.Select(a => a.UnitCount).ToArray()); // 20 -> 8, 8, 4 (D-33)
        Assert.Equal(new[] { 1, 2, 3 }, waves.Select(a => a.WaveIndex).ToArray());
        Assert.All(waves, w => Assert.Equal(3, w.WaveCount));
        Assert.All(waves, w => Assert.Equal(waves[0].SendId, w.SendId)); // one SendId shared across the send
    }

    [Fact]
    public void Send_Of8UnitsOrFewer_ProducesExactlyOneArmy_BitIdenticalToPreFR3Behavior()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(0, army.Id);
        Assert.Equal(8, army.UnitCount);
        Assert.Equal(0, army.LaunchTick);
        Assert.Equal(34, army.ArrivalTick); // same fixed travel time SendArmyTests already pins
        Assert.Equal(1, army.WaveIndex);
        Assert.Equal(1, army.WaveCount);
    }

    [Fact]
    public void Rejection_OfAnOversizedSend_LeavesNoPendingWaveBehind()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // 20 exceeds the starting garrison of 10 - rejected outright, before any splitting happens.
        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20));

        Assert.Equal(SendArmyOutcome.UnitCountExceedsGarrison, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);

        match.Advance(1000); // long enough to have launched every wave of a real 20-unit send
        Assert.Empty(match.ArmiesInFlight); // nothing was ever pending to launch
    }

    [Fact]
    public void PendingWaveOfTheirs_KeepsAPlayerAlive_EvenWithNoBasesAndNoLaunchedArmiesLeft()
    {
        // A real reproduction of this race (a player's last owned base falls at the same moment
        // their last *launched* army resolves, while a later wave of their own send still waits to
        // launch) needs two independent events landing on the same tick - not reachable through
        // ordinary play on the fixed map without contriving both sides' timing. Rigged directly by
        // reflection instead, the same style used throughout this file and RecaptureGraceTests/
        // CaptureDemotionTests for states unreachable through ordinary play.
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);

        // 16 units splits into two waves of 8; wave 1 enters ArmiesInFlight immediately, wave 2 sits
        // pending until tick 5 (D-35).
        SetGarrison(human, 16);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 16)));
        Assert.Single(match.ArmiesInFlight);

        // Simulate wave 1 having already resolved (destroyed in transit, say) by removing it
        // directly, and simulate the human's only base falling to an unrelated AI attack landing on
        // the same tick - together, zero owned bases and zero *launched* armies, with wave 2 still
        // the only thing keeping the human in the match.
        var armiesField = typeof(Match).GetField("_armies", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var armies = (List<Army>)armiesField.GetValue(match)!;
        armies.Clear();
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(human, new object?[] { match.AiPlayer });

        var evaluateOutcome = typeof(Match).GetMethod("EvaluateOutcome", BindingFlags.NonPublic | BindingFlags.Instance)!;
        evaluateOutcome.Invoke(match, null);

        Assert.Equal(MatchOutcome.InProgress, match.Outcome); // wave 2, still pending, keeps the human alive
    }

    // --- Pending launch (D-35) ---

    [Fact]
    public void PendingWaves_AreInvisibleAndUntargetable_UntilTheirOwnLaunchTick()
    {
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);

        SetGarrison(aiBase, 10000); // never captured - isolates the tower-fire question from combat
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, aiBase.Type);

        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 20)));

        // Immediately after Execute, only wave 1 exists - full-strength, since not a tick has
        // passed for the tower to have fired at it yet.
        var wave1 = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(1, wave1.WaveIndex);
        Assert.Equal(8, wave1.UnitCount);

        // Advance to one tick before wave 2's launch tick (5): wave 1 has been shot at (level-1
        // tower, period 6 - it may or may not have fired yet depending on range, but it is at most
        // wounded, never destroyed this early), and wave 2 is still nowhere to be seen.
        match.Advance(4);
        Assert.Single(match.ArmiesInFlight); // still only wave 1 - wave 2 has not launched

        // Advance exactly onto wave 2's launch tick: it appears, full-strength, having taken no
        // damage while pending - the tower could not have fired at what was not in ArmiesInFlight.
        match.Advance(1);
        var wave2 = match.ArmiesInFlight.Single(a => a.WaveIndex == 2);
        Assert.Equal(8, wave2.UnitCount); // untouched by the tower despite it having fired by now
    }

    [Fact]
    public void MatchDecided_BeforeAllWavesLaunch_FreezesTheRemainingPendingWaves()
    {
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Give the AI a second base first, so capturing its capital below does not itself end the
        // match before the column has even finished launching.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, neutral.Id, 8)));
        match.Advance(59); // the AI's own travel time to this neutral (SendArmyTests pins the human's shorter 34)
        Assert.Equal(match.AiPlayer, neutral.Owner);

        SetGarrisonAndResetProduction(aiBase, 1);
        SetGarrison(human, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 20)));
        Assert.Equal(3, SendWaveCalculator.WaveCount(20));

        var wave1 = match.ArmiesInFlight.Single(a => a.WaveIndex == 1);
        match.Advance(wave1.ArrivalTick - match.ElapsedTicks); // wave 1 captures aiBase
        Assert.Equal(match.HumanPlayer, aiBase.Owner);

        // Eliminate the AI outright by also taking its remaining base, deciding the match before
        // waves 2 and 3 (still pending) ever launch.
        SetGarrison(neutral, 0);
        SetGarrison(human, 1);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 1)));
        var finisher = match.ArmiesInFlight.Single(a => a.TargetBaseId == neutral.Id);
        match.Advance(finisher.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        match.Advance(1000); // long past waves 2 and 3's would-be launch ticks
        Assert.Empty(match.ArmiesInFlight); // neither ever launched - they are reported nowhere
    }

    // --- Per-wave resolution: capture, then reinforce ---

    [Fact]
    public void CaptureByOneWave_ThenReinforcementByLaterWavesInTheSameSend()
    {
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // A second AI base so eliminating aiBase does not end the match early (D-35).
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, neutral.Id, 8)));
        match.Advance(59); // the AI's own travel time to this neutral (SendArmyTests pins the human's shorter 34)
        Assert.Equal(match.AiPlayer, neutral.Owner);

        SetGarrisonAndResetProduction(aiBase, 1);
        SetGarrison(human, 30);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 24))); // 3 waves of 8

        var wave1 = match.ArmiesInFlight.Single(a => a.WaveIndex == 1);
        match.Advance(wave1.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, aiBase.Owner); // wave 1 captured it
        var afterWave1 = aiBase.GarrisonCount;

        var wave2 = match.ArmiesInFlight.Single(a => a.WaveIndex == 2);
        match.Advance(wave2.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, aiBase.Owner); // still human - wave 2 reinforced, did not fight
        Assert.Equal(afterWave1 + 8, aiBase.GarrisonCount);
        var afterWave2 = aiBase.GarrisonCount;

        var wave3 = match.ArmiesInFlight.Single(a => a.WaveIndex == 3);
        match.Advance(wave3.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(afterWave2 + 8, aiBase.GarrisonCount); // wave 3 reinforced too
    }

    [Fact]
    public void RecaptureGrace_AppliesMidColumn_BetweenTwoWavesOfTheSameSend()
    {
        // A genuine mid-flight retake between wave 1 and wave 2 is not reachable through ordinary
        // play on the fixed map: the gap between two waves is a fixed 5 ticks (WaveIntervalTicks),
        // while the map's shortest real inter-base travel is far longer, so nothing can ever arrive
        // and retake a base within 5 ticks of another army capturing it. The retake is rigged
        // directly by reflection instead - the same style RecaptureGraceTests and CaptureDemotionTests
        // already use for states unreachable through ordinary play - placed exactly inside the grace
        // window read back from wave 2's own real ArrivalTick.
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Give the AI a second base first (rigged directly, for the same reason as the retake
        // below), so wave 1 capturing aiBase does not itself end the match.
        SetOwner(neutral, match.AiPlayer);

        SetLevel(aiBase, 3);
        SetGarrisonAndResetProduction(aiBase, 1);
        SetGarrison(human, 30);

        // 16 units - two waves of 8 - captures aiBase on wave 1's arrival, demoting it.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 16)));
        var wave1 = match.ArmiesInFlight.Single(a => a.WaveIndex == 1);
        var wave2ArrivalTick = wave1.ArrivalTick + SendWaveCalculator.LaunchTickOffset(2);
        match.Advance(wave1.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(2, aiBase.Level); // demoted on first capture

        // Rig an instant AI retake, one tick before wave 2 arrives, well inside the grace window.
        SetOwner(aiBase, match.AiPlayer);
        SetOwnerBeforeLastChange(aiBase, match.AiPlayer); // the AI held it immediately before the human's capture
        SetLastOwnerChangeTick(aiBase, wave2ArrivalTick - 1 - 5);
        SetGarrisonAndResetProduction(aiBase, 1); // pin the retaken hold steady so wave 2's 8 units are unambiguously enough

        // Wave 2, from the human's original 16-unit send, now lands against the rigged AI owner and
        // demotes it normally - the grace protects only the retake itself, never a later, unrelated
        // capture by someone else.
        var wave2 = match.ArmiesInFlight.Single(a => a.WaveIndex == 2);
        Assert.Equal(match.HumanPlayer, wave2.Owner);
        Assert.Equal(wave2ArrivalTick, wave2.ArrivalTick);
        match.Advance(wave2.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, aiBase.Owner); // wave 2 re-captures it
        Assert.Equal(1, aiBase.Level); // this second capture demotes normally - no grace protects it
    }

    // --- Combat: a column is deliberately weaker than one big arrival ---

    [Fact]
    public void TenEightUnitWaves_DoLessTotalDamage_ThanOneEightyUnitArrival()
    {
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);

        SetLevel(aiBase, 5); // village ladder tops at 140% defence, D-29 (only reachable by rigging - MaxUpgradableLevel is 4)
        SetGarrison(aiBase, 1_000_000); // far above the level-5 cap: never captured, never produces (so nothing but combat moves this number)
        SetGarrison(human, 80);

        // FR-2: captured before the send fights, since the column's own combat will move both
        // players' morale as it goes (the defender gains from every attacking unit destroyed,
        // D-41) - the fair "same 80 units, one arrival instead of many" comparison below holds
        // every other condition equal, including the morale each side carried at the moment the
        // send was issued, not whatever it drifted to after several waves of combat.
        var startingAttackerMoralePercent = MoraleTable.AttackPercentage(match.HumanMorale.Level);
        var startingDefenderMoralePercent = MoraleTable.DefencePercentage(match.AiMorale.Level);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 80)));
        var waveCount = SendWaveCalculator.WaveCount(80);
        Assert.Equal(10, waveCount);

        var before = aiBase.GarrisonCount;
        for (var waveIndex = 1; waveIndex <= waveCount; waveIndex++)
        {
            var wave = match.ArmiesInFlight.Single(a => a.WaveIndex == waveIndex);
            match.Advance(wave.ArrivalTick - match.ElapsedTicks);
        }

        var columnDamage = before - aiBase.GarrisonCount;

        // Phase 6 FR-3: neither player owns a forge in this scenario, so both terms are ForgeTable's
        // identity and the comparison below is unchanged.
        var attackerIndex = CombatResolver.ComposeAttackerIndex(
            startingAttackerMoralePercent,
            ForgeTable.AttackPercentage(ForgeTable.MinForgeCount));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(
            LevelTable.Village.DefencePercentage(5),
            startingDefenderMoralePercent,
            ForgeTable.DefencePercentage(ForgeTable.MinForgeCount));
        var singleArrivalResult = CombatResolver.Resolve(attackerIndex, defenderIndex, 80, 1_000_000);
        var singleArrivalDamage = 1_000_000 - singleArrivalResult.RemainingGarrison;

        Assert.Equal(match.AiPlayer, aiBase.Owner); // held throughout - the huge garrison never falls
        Assert.True(columnDamage < singleArrivalDamage, "a column of waves must do strictly less total damage than one arrival of the same size");
    }

    [Fact]
    public void EightyUnitColumn_PastALevel1Tower_TakesMoreTransitLossesThanASingleArrivalCould_AndTheBaseHolds()
    {
        var match = new Match();
        var human = HumanBase(match);
        var aiBase = AiBase(match);

        SetGarrison(aiBase, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, aiBase.Type);
        SetGarrison(aiBase, 50); // enough that even the weakened column below cannot take it

        SetGarrison(human, 80);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, aiBase.Id, 80)));
        var waveCount = SendWaveCalculator.WaveCount(80);

        var totalArrivedAlive = 0;
        for (var waveIndex = 1; waveIndex <= waveCount; waveIndex++)
        {
            var wave = match.ArmiesInFlight.Single(a => a.WaveIndex == waveIndex);
            match.Advance(wave.ArrivalTick - match.ElapsedTicks - 1);
            totalArrivedAlive += wave.UnitCount;
            match.Advance(1); // resolves this wave's arrival (reinforcement or a repelled attack)
        }

        var totalTransitLosses = 80 - totalArrivedAlive;

        var rangeTicks = LevelTable.Tower.RangeUnits(LevelTable.MinLevel) / Match.ArmySpeedUnitsPerTick;
        var singleArmyShots = (long)(rangeTicks / LevelTable.Tower.FirePeriodTicks(LevelTable.MinLevel));

        Assert.True(totalTransitLosses > singleArmyShots,
            "a column staggered across many waves must spend strictly more total time in tower range than one army passing through once");
        Assert.Equal(match.AiPlayer, aiBase.Owner); // the base is still held at the end
    }

    // --- Determinism ---

    [Fact]
    public void MultiWaveSend_SingleCallAndIrregularChunks_AgreeOnEveryFieldIncludingWhichWaveCaptured()
    {
        var oneCall = new Match();
        Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(oneCall.Outcome, chunked.Outcome);
        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.LastOwnerChangeTick)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.LastOwnerChangeTick)));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Id, a.Owner, a.UnitCount, a.LaunchTick, a.ArrivalTick, a.SendId, a.WaveIndex, a.WaveCount)),
            chunked.ArmiesInFlight.Select(a => (a.Id, a.Owner, a.UnitCount, a.LaunchTick, a.ArrivalTick, a.SendId, a.WaveIndex, a.WaveCount)));
    }

    /// <summary>Sends a 20-unit column (3 waves) that captures a neutral, then lets a second, smaller send play out too.</summary>
    private static void Play(Match match, Action<long> advance)
    {
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 20);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20)));
        var waveCount = SendWaveCalculator.WaveCount(20);
        var lastWaveLaunch = SendWaveCalculator.LaunchTickOffset(waveCount);
        advance(lastWaveLaunch + 34 + 5); // past every wave's arrival

        advance(60); // let the capital regrow a little
        SetGarrison(human, 3);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 3)));
        advance(34);
    }

    private static void AdvanceInIrregularChunks(Match match, long ticks)
    {
        var remaining = ticks;
        var sizes = new long[] { 1, 7, 3, 2, 11, 4 };
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
