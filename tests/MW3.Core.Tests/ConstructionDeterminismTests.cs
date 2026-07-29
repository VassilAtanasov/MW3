namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12) over FR-3c's construction state: levels, types, garrisons, production
/// progress, construction state, and owner-change ticks must agree whatever chunk sizes
/// <see cref="Match.Advance"/> is called with. The scripted run drives all three things the
/// acceptance criteria call out together: a build that completes, a build lost to a capture, and a
/// recapture inside the grace window - reached through real commands and the map's real, fixed
/// travel times, not reflection: two AI waves are launched toward the same neutral base at different
/// ticks (one before the other even resolves) so their arrivals land where the script needs them,
/// exactly the way a human player timing two waves to land close together would.
/// </summary>
public class ConstructionDeterminismTests
{
    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level, long Progress, long? Construction, long? LastOwnerChangeTick)[] Snapshot(Match match) =>
        match.Bases.Select(b => (
            b.Id,
            b.Owner,
            b.Type,
            b.GarrisonCount,
            b.Level,
            b.ProductionProgressTicks,
            b.Construction?.CompletionTick,
            b.LastOwnerChangeTick)).ToArray();

    /// <summary>
    /// Drives one match through a scripted run. Comments give the absolute elapsed tick each step
    /// lands on - the capital-to-neutral travel times (59 ticks from the AI, 34 from the human, to
    /// the first neutral base in the map layout) are fixed by the map's geometry.
    /// </summary>
    private static int Play(Match match, Action<long> advance)
    {
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var target = match.Bases.First(b => b.Owner is null);

        advance(600); // t600: both capitals sit at their level-1 cap of 20

        // Wave 1: the AI takes the neutral base.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));

        advance(45); // t645

        // Wave 3 (pre-positioned): launched now, before the base has even fallen to the AI, timed to
        // land at exactly t704 - twenty ticks after wave 2 below is due to capture it back for the
        // human. This is the recapture-within-grace wave.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));

        advance(5); // t650

        // Wave 2: the human's smaller reply, timed to land at t684.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, target.Id, 3)));

        advance(20); // t670 - wave 1 (due at t659) has already resolved along the way
        Assert.Equal(match.AiPlayer, target.Owner);

        // A build the AI starts on its new base and then loses to wave 2, seconds before it would
        // otherwise complete - "a build lost to a capture".
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, target.Id)));
        Assert.NotNull(target.Construction);

        advance(14); // t684 - wave 2 lands
        Assert.Equal(match.HumanPlayer, target.Owner);
        Assert.Null(target.Construction); // discarded with the capture, never completed for the AI

        advance(20); // t704 - wave 3 lands: a retake by the AI, exactly 20 ticks after t684
        Assert.Equal(match.AiPlayer, target.Owner);

        // A build that completes normally, on the base the AI just retook.
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, target.Id)));

        advance(150); // comfortably past the 100-tick build's completion at t804
        Assert.Equal(LevelTable.MinLevel + 1, target.Level);

        return target.Id;
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnEveryPieceOfConstructionState()
    {
        var oneCall = new Match();
        var targetId = Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));

        var target = oneCall.Bases.Single(b => b.Id == targetId);
        Assert.Equal(oneCall.AiPlayer, target.Owner);
        Assert.Equal(LevelTable.MinLevel + 1, target.Level);
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
