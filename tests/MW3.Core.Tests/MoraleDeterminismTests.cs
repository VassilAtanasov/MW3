namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12, S-8) over FR-1's accrual: a run exercising a capture, arrival-combat deaths,
/// and a completed upgrade must land on identical morale for both players whether
/// <see cref="Match.Advance"/> runs in one call or in irregular chunks, following
/// <see cref="AiTowerRoutingDeterminismTests"/>'s pattern with morale added to the projection.
/// </summary>
public class MoraleDeterminismTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level)).ToArray();

    /// <summary>
    /// Sends the human against a neutral (capture accrual), the AI against the human's home base
    /// (a failed attack - death accrual in both directions), and upgrades the human's captured base
    /// to completion (upgrade accrual) - the three accrual sites FR-1 adds that read the tick, all
    /// in one run.
    /// </summary>
    private static void Play(Match match)
    {
        var human = match.HumanPlayer;
        var ai = match.AiPlayer;
        var humanBase = match.Bases.Single(b => b.Owner == human);
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetGarrison(humanBase, 100);
        SetGarrison(aiBase, 100);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(human, humanBase.Id, neutral.Id, 10)));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 5)));

        match.Advance(200); // past both sends' every wave, including the neutral's reinforcing wave

        Assert.Equal(human, neutral.Owner); // capture accrual exercised
        Assert.Equal(human, humanBase.Owner); // AI's attack failed - death accrual exercised both ways

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(human, neutral.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel)); // upgrade accrual exercised
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnMoralePoints()
    {
        var oneCall = new Match();
        Play(oneCall);

        var chunked = new Match();
        PlayInChunks(chunked);

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));

        Assert.Equal(oneCall.HumanMorale.Points, chunked.HumanMorale.Points);
        Assert.Equal(oneCall.HumanMorale.Level, chunked.HumanMorale.Level);
        Assert.Equal(oneCall.AiMorale.Points, chunked.AiMorale.Points);
        Assert.Equal(oneCall.AiMorale.Level, chunked.AiMorale.Level);

        // Sanity: both morale totals actually moved, so this proves agreement on real accrual, not
        // on two runs that both stayed at zero.
        Assert.NotEqual(0, oneCall.HumanMorale.Points);
    }

    private static void PlayInChunks(Match match)
    {
        var human = match.HumanPlayer;
        var ai = match.AiPlayer;
        var humanBase = match.Bases.Single(b => b.Owner == human);
        var aiBase = match.Bases.Single(b => b.Owner == ai);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetGarrison(humanBase, 100);
        SetGarrison(aiBase, 100);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(human, humanBase.Id, neutral.Id, 10)));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(ai, aiBase.Id, humanBase.Id, 5)));

        AdvanceInIrregularChunks(match, 200);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(human, neutral.Id)));
        AdvanceInIrregularChunks(match, LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
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
