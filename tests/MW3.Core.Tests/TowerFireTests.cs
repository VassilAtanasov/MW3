namespace MW3.Core.Tests;

/// <summary>
/// FR-4 (D-24): a tower removes one unit from an enemy army every N ticks while it is within range,
/// and an army whose strength reaches zero is destroyed and never arrives. Armies are built up
/// through real <see cref="ConvertCommand"/>/<see cref="SendArmyCommand"/> calls wherever the
/// scenario allows it; a handful of corner cases (an already-eliminated player's last base, a
/// specific pre-set level) are rigged by reflection, the same style <see cref="CaptureDemotionTests"/>
/// already uses for states ordinary play cannot reach quickly.
/// </summary>
public class TowerFireTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    /// <summary>Converts <paramref name="b"/> to a tower via a real, garrison-affordable command and advances past its build.</summary>
    private static void ConvertToTower(Match match, Player owner, Base b)
    {
        SetGarrison(b, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(owner, b.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, b.Type);
    }

    [Fact]
    public void OwnedTower_DestroysASmallEnemyArmy_BeforeItArrives()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);

        SetGarrison(aiBase, 4); // small enough that the level-1 tower destroys the whole wave in flight
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();
        var garrisonBeforeFlight = humanBase.GarrisonCount;

        match.Advance(army.ArrivalTick - match.ElapsedTicks + 5); // well past the original arrival tick

        Assert.Empty(match.ArmiesInFlight); // destroyed, not merely stalled
        Assert.Equal(match.HumanPlayer, humanBase.Owner); // never captured
        Assert.Equal(garrisonBeforeFlight, humanBase.GarrisonCount); // nothing delivered - no reinforcement, no attack
    }

    [Fact]
    public void OwnedTower_FiresRegardlessOfItsOwnGarrison()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);
        SetGarrison(humanBase, 0); // a garrison is not ammunition (FR-3)

        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var wave = match.ArmiesInFlight.Single();
        match.Advance(wave.ArrivalTick - match.ElapsedTicks + 5); // well past its own arrival/destruction

        Assert.NotNull(humanBase.LastFireTick); // it fired even while completely empty
        Assert.Empty(match.ArmiesInFlight); // and it destroyed the wave outright
        Assert.Equal(match.HumanPlayer, humanBase.Owner); // never taken - a drained tower still shoots
    }

    [Fact]
    public void Tower_NeverFiresAtItsOwnersArmies()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Capture the neutral first, so the human has a second base to reinforce from through the
        // tower's own range.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 8)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        ConvertToTower(match, match.HumanPlayer, humanBase);
        SetGarrison(neutral, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 8)));
        var army = match.ArmiesInFlight.Single(); // 8 units or fewer never splits into waves (FR-3)
        var beforeGarrison = humanBase.GarrisonCount;
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(beforeGarrison + 8, humanBase.GarrisonCount); // the whole reinforcement arrived
        Assert.Null(humanBase.LastFireTick); // never fired at all - its own owner's army passed through untouched
    }

    [Fact]
    public void Tower_FiresAtTheNearestOfTwoInRangeArmies_TiesBrokenByLowestId()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);

        // Two identical waves launched on the same tick from the same source to the same target are
        // at the exact same distance from the tower on every shared tick - a genuine tie, broken only
        // by id (the first one sent, launched first in the same call).
        SetGarrison(aiBase, 20);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 5)));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 5)));
        var armies = match.ArmiesInFlight.OrderBy(a => a.Id).ToList();
        var first = armies[0];
        var second = armies[1];

        match.Advance(1); // however many ticks it takes to enter range and fire once is irrelevant - just enough for one shot
        while (first.UnitCount == 5 && second.UnitCount == 5 && match.ElapsedTicks < first.ArrivalTick)
        {
            match.Advance(1);
        }

        Assert.Equal(4, first.UnitCount); // hit
        Assert.Equal(5, second.UnitCount); // untouched
    }

    [Fact]
    public void DestroyingAPlayersLastArmy_EliminatesThemIfTheyAlsoOwnNoBases()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);

        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));

        // The AI has already lost every base by the time its last army is shot down - rigged
        // directly, since reaching this through real captures would also destroy the very army this
        // test needs to still be in flight.
        SetOwner(aiBase, match.HumanPlayer);

        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks + 5);

        Assert.Empty(match.ArmiesInFlight);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);
    }

    [Fact]
    public void ConvertedBackToProducer_StopsFiring_FromThatTick()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);
        SetGarrison(humanBase, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Producer)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Producer, humanBase.Type);

        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(4, army.UnitCount); // untouched - no longer a tower, so it never fired
    }

    [Fact]
    public void CapturedTower_FiresForItsNewOwner_FromThatTick_EvenAgainstItsFormerOwner()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // The AI keeps a second base (a captured neutral) so it still has somewhere to send from
        // after its capital falls.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, neutral.Id, 8)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.AiPlayer, neutral.Owner);

        ConvertToTower(match, match.AiPlayer, aiBase);
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);

        // The human captures the AI's tower outright. 8 units - the largest send that stays a
        // single wave (FR-3) - is overwhelming against a garrison of 1.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 8)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(BaseType.Tower, aiBase.Type); // capture keeps the type (D-23)

        // The AI, now attacking its former tower from its remaining base, is shot at by it just like
        // any other tower.
        SetGarrison(neutral, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, neutral.Id, aiBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks + 5);

        Assert.Empty(match.ArmiesInFlight); // the AI's own former tower shot its army down
    }

    /// <summary>
    /// A base converting to a tower fires on the very tick its build completes, if an enemy army is
    /// already in range that tick - construction completion happens before tower fire within the
    /// same tick (D-30, D-24).
    /// </summary>
    [Fact]
    public void ABaseConvertingToATower_FiresOnTheExactTickItsBuildCompletes()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        SetGarrison(humanBase, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        var completionTick = match.ElapsedTicks + LevelTable.ConversionBuildDurationTicks;

        // Time the AI's launch so its army is inside the level-1 range (entering roughly 20 ticks
        // before its own arrival, given the fixed 0.01 units/tick speed) exactly on the completion
        // tick - proving the fresh tower's very first eligible tick is the one it completes on.
        var travelTicks = 76; // the fixed capital-to-capital distance

        // Entry into the level-1 range happens 20 ticks before arrival (range 0.20 at 0.01
        // units/tick); an arrival of completionTick + 15 puts entry 5 ticks before the build
        // completes, so the army is already well inside range by the time the tower exists.
        var desiredArrival = completionTick + 15;
        match.Advance(desiredArrival - travelTicks - match.ElapsedTicks);
        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();
        Assert.Equal(desiredArrival, army.ArrivalTick);

        // Stop exactly on the completion tick - advancing further would let the tower (level 1,
        // period 6) fire again before the army arrives, moving LastFireTick past the one tick this
        // test is actually about.
        match.Advance(completionTick - match.ElapsedTicks);

        Assert.Equal(BaseType.Tower, humanBase.Type);
        Assert.Equal(completionTick, humanBase.LastFireTick);
        Assert.Equal(3, army.UnitCount); // it was already hit, once, on this very tick
    }

    /// <summary>
    /// A full-strength army flying straight at a tower and arriving at it always loses at least one
    /// unit in transit, at every level (FR-3: re-authored against an 8-unit send, the largest that
    /// stays a single wave, rather than the pre-FR-3 100-unit figure a wave column can no longer
    /// produce as one army).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TuningSanity_UnitsLostFlyingStraightAtATower_IsRoughlyTheStatedApproximation(int level)
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);

        for (var l = LevelTable.MinLevel; l < level; l++)
        {
            SetGarrison(humanBase, LevelTable.UpgradeCost(BaseType.Tower, l));
            Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
            match.Advance(LevelTable.UpgradeBuildDurationTicks(l));
        }

        Assert.Equal(level, humanBase.Level);

        SetGarrison(aiBase, 8);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 8)));
        var army = match.ArmiesInFlight.Single(); // 8 units or fewer never splits into waves (FR-3)
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var survived = match.ArmiesInFlight.Any() ? army.UnitCount : 0; // a high enough level can wipe it out entirely
        var lost = 8 - survived;

        Assert.True(lost > 0, "the tower must land at least one hit over the full transit");
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnArmyStrengthsAndOutcome()
    {
        var oneCall = new Match();
        Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(oneCall.Outcome, chunked.Outcome);
        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.LastFireTick)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.LastFireTick)));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Id, a.Owner, a.UnitCount, a.LaunchTick, a.ArrivalTick, a.SendId, a.WaveIndex, a.WaveCount)),
            chunked.ArmiesInFlight.Select(a => (a.Id, a.Owner, a.UnitCount, a.LaunchTick, a.ArrivalTick, a.SendId, a.WaveIndex, a.WaveCount)));
    }

    /// <summary>
    /// Converts the human base to a tower, then sends two AI attacks through it: one small enough
    /// to be destroyed outright as a single wave, and one large enough to split into a multi-wave
    /// column (FR-3) whose combined force still takes the base.
    /// </summary>
    private static void Play(Match match, Action<long> advance)
    {
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!
            .Invoke(humanBase, new object?[] { LevelTable.ConversionCost + 20 });
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);

        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(aiBase, new object?[] { 4 });
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var destroyed = match.ArmiesInFlight.Single();
        advance(destroyed.ArrivalTick - match.ElapsedTicks + 5);
        Assert.Empty(match.ArmiesInFlight);

        advance(60); // let the AI base recover a little garrison
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(aiBase, new object?[] { 40 });
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 40)));
        var firstWave = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).First();
        var waveCount = SendWaveCalculator.WaveCount(40);
        var lastWaveArrival = firstWave.ArrivalTick + SendWaveCalculator.LaunchTickOffset(waveCount);
        advance(lastWaveArrival - match.ElapsedTicks + 5); // past every wave's arrival

        Assert.Equal(match.AiPlayer, humanBase.Owner); // the surviving wave was strong enough to take it
    }

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
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
