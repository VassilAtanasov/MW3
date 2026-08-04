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

        // The human base upgrades too, early, so its cap (and so its eventual attacking force) is
        // large enough to take a level-2 tower (170% defence) at the end of the script however
        // little the AI's own spending has left it holding.
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        // The AI base must earn its way up to level 3 first: a level-1 village's cap (20) cannot
        // afford the 30-unit conversion cost, and the garrison left over after converting must still
        // cover the tower's own 20-unit upgrade cost.
        advance(60); // level 1 at 60 ticks/unit: 10 + 1 = 11
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));
        advance(LevelTable.UpgradeBuildDurationTicks(1)); // the 100-tick build completes: level 2
        Assert.Equal(2, aiBase.Level);

        advance(720);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));
        advance(LevelTable.UpgradeBuildDurationTicks(2)); // the 200-tick build completes: level 3
        Assert.Equal(3, aiBase.Level);

        advance(600);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        advance(LevelTable.ConversionBuildDurationTicks); // the 100-tick build completes: a level-1 tower
        Assert.Equal(BaseType.Tower, aiBase.Type);
        Assert.Equal(LevelTable.MinLevel, aiBase.Level);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.AiPlayer, aiBase.Id)));
        advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel)); // the tower's own 100-tick build: level 2
        Assert.Equal(2, aiBase.Level);

        // FR-2: every upgrade above has been banking morale for both sides (neither yet reaching
        // morale level 1's 500-point threshold), and the tower defends at MW2's Bu = (a/d) x Wu
        // with waves capped at SendWaveCalculator.WaveSizeUnits (8) - a single wave of 8 can never
        // clear a 170%-defended garrison outright, so the assault must run over several waves, and
        // each failed wave feeds the defender +10 morale per attacking unit destroyed (D-41). A
        // second human upgrade (to level 3, cap 60) is needed so the assault carries enough waves
        // to finish the capture before the defender's climbing morale (still under threshold
        // throughout, given the numbers below) makes it unwinnable within the whole send.
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        advance(LevelTable.UpgradeBuildDurationTicks(2)); // the 200-tick build completes: level 3
        Assert.Equal(3, humanBase.Level);
        advance(1200); // refill toward the level-3 cap of 60

        // The human base send its whole garrison (level-3 cap of 60, comfortably above the level-1
        // cap of 20 the original single-upgrade script relied on) - enough waves survive the
        // tower's climbing morale defence to finish the capture.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, humanBase.GarrisonCount)));
        advance(400); // long enough for every wave to land and the capture to resolve

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
