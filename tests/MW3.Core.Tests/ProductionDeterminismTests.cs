namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12) over the state this feature adds: levels, caps, and production progress must
/// agree whatever chunk sizes <see cref="Match.Advance"/> is called with, not just garrison counts.
/// </summary>
public class ProductionDeterminismTests
{
    private static (int Id, Player? Owner, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();

    /// <summary>
    /// Drives one match through a scripted run that deliberately contains all three of the things
    /// this feature can get wrong under chunking: a base that reaches and sits at its cap, an
    /// upgrade partway through a production period, and a capture that demotes a base.
    /// </summary>
    private static void Play(Match match, Action<long> advance)
    {
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        advance(6);
        // Every outcome is asserted, so the scenario cannot quietly stop exercising what it claims:
        // a rejected upgrade would otherwise be rejected identically in both matches and the test
        // would still pass while no longer covering an upgrade at all.
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        advance(300); // long enough to sit at the cap
        Assert.Equal(humanBase.GarrisonCap, humanBase.GarrisonCount);

        Assert.Equal(
            SendArmyOutcome.Accepted,
            match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 20)));

        advance(400);
        Assert.Equal(match.HumanPlayer, neutral.Owner); // the capture really happened
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, neutral.Id)));
        advance(250);
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnLevelsCapsGarrisonsAndProgress()
    {
        var oneCall = new Match();
        Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));

        // The run must actually exercise what it claims to: a levelled-up base and a captured one.
        Assert.Contains(oneCall.Bases, b => b.Level > LevelTable.MinLevel);
        Assert.Equal(2, oneCall.Bases.Count(b => b.Owner == oneCall.HumanPlayer));
    }

    [Fact]
    public void CapIsReachedAtTheSameAbsoluteState_HoweverTheSpanIsSplit()
    {
        var oneCall = new Match();
        oneCall.Advance(137);

        var chunked = new Match();
        foreach (var chunk in new long[] { 1, 4, 2, 10, 20, 100 })
        {
            chunked.Advance(chunk);
        }

        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(1000)]
    public void ProgressAndGarrison_AgreeAcrossEveryBoundaryAroundTheCap(long totalTicks)
    {
        var oneCall = new Match();
        oneCall.Advance(totalTicks);

        var chunked = new Match();
        for (var i = 0L; i < totalTicks; i++)
        {
            chunked.Advance(1); // the finest possible chunking - one tick at a time
        }

        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));
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
