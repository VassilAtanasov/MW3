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
    /// Wraps <see cref="AiBrain"/> and records every send it decides, so a determinism run can
    /// prove which of its own attack decisions actually fired rather than only comparing the two
    /// run modes' end states to each other (#56).
    /// </summary>
    private sealed class RecordingBrain : IPlayerBrain
    {
        private readonly AiBrain _inner;

        public RecordingBrain(Player player) => _inner = new AiBrain(player);

        public Player Player => _inner.Player;

        public List<SendArmyCommand> SentCommands { get; } = new();

        public BrainDecision Decide(Match match)
        {
            var decision = _inner.Decide(match);
            if (decision.IsSend)
            {
                SentCommands.Add(decision.Command);
            }

            return decision;
        }
    }

    /// <summary>
    /// Rigs a front-base saturation (ripe for TryConvert on the very next decision tick) and a
    /// nearby enemy tower (ripe for TryAttack's loss-aware target preference), then lets the AI play
    /// on for several decision ticks - the two FR-7 behaviors a determinism run must exercise
    /// together, alongside the pre-existing send/upgrade behavior the initial capture already uses.
    /// Every other not-owned base is made unwinnable so TryAttack's only viable target is neutral4
    /// itself - the enemy tower - forcing the loss-aware branch, not a tower-oblivious pick of an
    /// easier target, to be the one that fires (#56).
    /// </summary>
    private static void Play(MatchRunner runner, Action<long> advance)
    {
        var match = runner.Match;
        var ai = match.AiPlayer;
        var human = match.HumanPlayer;
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral2 = match.Bases[2];
        var neutral3 = match.Bases[3];
        var neutral4 = match.Bases[4];
        var neutral5 = match.Bases[5];

        // 7, not 6 (phase 6 FR-2): base 5 sits within the shipped map's new neutral tower's range,
        // so one attacking unit is lost to tower fire during the final approach before this capture
        // resolves.
        Assert.Equal(SendArmyOutcome.Accepted, runner.Execute(new SendArmyCommand(ai, aiBase.Id, neutral5.Id, 7)));
        advance(34); // below the 40-tick decision interval: no AI decision fires before the rig below
        Assert.Equal(ai, neutral5.Owner);

        // Level pinned to MaxUpgradableLevel so aiBase can never be an upgrade candidate (only a
        // convert one); garrison just above ConversionCost (30), not its garrison cap, so the
        // leftover after paying the cost (2) is too small to ever outcompete neutral5 below for
        // TryAttack's descending-by-garrison source order - see #56: an oversized leftover here
        // previously let aiBase win every attack with a margin many times the ~3-unit tower loss,
        // which meant the run never actually exercised a decision the loss estimate was pivotal to.
        SetLevel(aiBase, LevelTable.MaxUpgradableLevel(BaseType.Producer));
        SetGarrison(aiBase, 32);

        SetOwner(neutral4, human);
        SetType(neutral4, BaseType.Tower);
        SetLevel(neutral4, LevelTable.MinLevel);
        SetGarrison(neutral4, 5);

        // unclampedHalf 9, minus the ~3-unit estimated tower loss, still > neutral4's 5 - the same
        // margin AiBrainTests.TryAttack_OnlyViableTargetBehindATower_IsStillAttacked_... pins down
        // directly - and below the level-1 producer cap of 20, so it stays an attack-only source
        // (never an upgrade or convert candidate).
        SetGarrison(neutral5, 18);
        SetGarrison(match.Bases[0], 1000); // human home base: unwinnable regardless of unclampedHalf
        SetGarrison(neutral2, 1000);
        SetGarrison(neutral3, 1000);

        advance(MatchRunner.DecisionIntervalTicks * 8);
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnConvertsAndTowerAwareAttacks()
    {
        var oneCall = new Match();
        var oneCallBrain = new RecordingBrain(oneCall.AiPlayer);
        var oneCallRunner = new MatchRunner(oneCall, oneCallBrain);
        Play(oneCallRunner, oneCallRunner.Advance);

        var chunked = new Match();
        var chunkedBrain = new RecordingBrain(chunked.AiPlayer);
        var chunkedRunner = new MatchRunner(chunked, chunkedBrain);
        Play(chunkedRunner, ticks => AdvanceInIrregularChunks(chunkedRunner, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(oneCall.Outcome, chunked.Outcome);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)),
            chunked.ArmiesInFlight.Select(a => (a.Owner, a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.LaunchTick, a.ArrivalTick)));

        // Sanity: the rigged scenario genuinely exercised the convert clause, not just the parts of
        // Decide that already existed before FR-7. (Named by id, not a generic lookup: a
        // successful attack below can leave the AI owning a second tower - the captured neutral4 -
        // so a lookup with no id would no longer be guaranteed unique.)
        const int aiBaseId = 1;
        Assert.Equal(BaseType.Tower, oneCall.Bases.Single(b => b.Id == aiBaseId).Type);

        // #56: prove the tower-aware attack branch itself fired, in both run modes - not just that
        // whatever happened agreed between them. neutral4 (id 4) is both the target and the enemy
        // tower here, rigged as a level-1 tower before the run, so any send the AI issued against
        // its id is a send whose target sat within that tower's own range at send time - the
        // scenario leaves no easier, tower-oblivious target winnable.
        const int neutral4Id = 4;
        Assert.Contains(oneCallBrain.SentCommands, c => c.TargetBaseId == neutral4Id);
        Assert.Contains(chunkedBrain.SentCommands, c => c.TargetBaseId == neutral4Id);
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
