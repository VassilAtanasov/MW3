namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12) over conversion: a run containing a producer-to-tower conversion, the upgrade
/// of a tower, and the capture of a tower must agree on types, levels, garrisons, and production
/// progress whether <see cref="Match.Advance"/> runs in one call or in irregular chunks.
/// </summary>
public class ConvertDeterminismTests
{
    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();

    /// <summary>
    /// Drives one match through a scripted run: bank some production on both sides, convert the AI's
    /// base to a tower, upgrade it while it is a tower, then capture it with the human's base - the
    /// three things FR-3 adds that a determinism run must exercise together.
    /// </summary>
    private static int Play(Match match, Action<long> advance)
    {
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        // The AI base must earn its way up to level 3 first: a level-1 village's cap (20) cannot
        // afford the 30-unit conversion cost, and the garrison left over after converting must still
        // cover the tower's own 20-unit upgrade cost.
        advance(60); // level 1 at 60 ticks/unit: 10 + 1 = 11
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));

        advance(720); // level 2 at 30 ticks/unit: 6 + 24 = 30
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));

        advance(600); // level 3 at 20 ticks/unit: 20 + 30 = 50
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        advance(400); // long enough for the send to land and the capture to resolve

        Assert.Equal(match.HumanPlayer, aiBase.Owner); // the capture really happened
        advance(250);

        return aiBase.Id;
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnTypesLevelsGarrisonsAndProgress()
    {
        var oneCall = new Match();
        var capturedId = Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));

        var capturedTower = oneCall.Bases.Single(b => b.Id == capturedId);
        Assert.Equal(oneCall.HumanPlayer, capturedTower.Owner);
        Assert.Equal(BaseType.Tower, capturedTower.Type); // capture kept the type
        Assert.Equal(LevelTable.MinLevel, capturedTower.Level); // demoted by one from the level-2 upgrade
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
