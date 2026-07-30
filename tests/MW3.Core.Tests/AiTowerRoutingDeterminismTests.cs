namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12) over FR-7: a run containing a front-base saturation ripe for
/// <c>AiBrain.TryConvert</c> and a nearby enemy tower that changes <c>AiBrain.TryAttack</c>'s
/// target preference must agree on every base and army, in full, whether
/// <see cref="MatchRunner.Advance"/> runs in one call or in irregular chunks.
/// </summary>
public class AiTowerRoutingDeterminismTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetType(Base b, BaseType type) =>
        typeof(Base).GetProperty(nameof(Base.Type))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { type });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level, long? LastFireTick)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.LastFireTick)).ToArray();

    /// <summary>
    /// Rigs a front-base saturation (ripe for TryConvert on the very next decision tick) and a
    /// nearby enemy tower (ripe for TryAttack's loss-aware target preference), then lets the AI play
    /// on for several decision ticks - the two FR-7 behaviors a determinism run must exercise
    /// together, alongside the pre-existing send/upgrade behavior the initial capture already uses.
    /// </summary>
    private static void Play(MatchRunner runner, Action<long> advance)
    {
        var match = runner.Match;
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral4 = match.Bases[4];
        var neutral5 = match.Bases[5];

        Assert.Equal(SendArmyOutcome.Accepted, runner.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 6)));
        advance(34); // below the 40-tick decision interval: no AI decision fires before the rig below
        Assert.Equal(ai, neutral5.Owner);

        SetLevel(aiBase, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        SetGarrison(aiBase, LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MaxUpgradableLevel(BaseType.Producer))!.Value);

        SetOwner(neutral4, human);
        SetType(neutral4, BaseType.Tower);
        SetLevel(neutral4, LevelTable.MinLevel);
        SetGarrison(neutral4, 5);

        advance(MatchRunner.DecisionIntervalTicks * 8);
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnConvertsAndTowerAwareAttacks()
    {
        var oneCall = new Match();
        var oneCallRunner = new MatchRunner(oneCall, new AiBrain(oneCall.AiPlayer));
        Play(oneCallRunner, oneCallRunner.Advance);

        var chunked = new Match();
        var chunkedRunner = new MatchRunner(chunked, new AiBrain(chunked.AiPlayer));
        Play(chunkedRunner, ticks => AdvanceInIrregularChunks(chunkedRunner, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(oneCall.Outcome, chunked.Outcome);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)),
            chunked.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)));

        // Sanity: the rigged scenario genuinely exercised the convert clause, not just the parts of
        // Decide that already existed before FR-7.
        var aiOwnedTower = oneCall.Bases.SingleOrDefault(b => b.Owner == oneCall.AiPlayer && b.Type == BaseType.Tower);
        Assert.NotNull(aiOwnedTower);
    }

    private static void AdvanceInIrregularChunks(MatchRunner runner, long ticks)
    {
        var remaining = ticks;
        var sizes = new long[] { 1, 7, 3, 40, 2, 113 };
        var i = 0;
        while (remaining > 0)
        {
            var chunk = Math.Min(sizes[i % sizes.Length], remaining);
            runner.Advance(chunk);
            remaining -= chunk;
            i++;
        }
    }
}
