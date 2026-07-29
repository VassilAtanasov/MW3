namespace MW3.Core.Tests;

/// <summary>
/// Determinism (D-12) over <see cref="CombatResolver"/>'s ratio formula (D-29): a run containing an
/// attack on an upgraded base, an attack on a tower, and a capture must agree on garrisons, owners,
/// levels, types, and production progress whether <see cref="Match.Advance"/> runs in one call or in
/// irregular chunks - the same style <see cref="ConvertDeterminismTests"/> already uses for FR-3.
/// </summary>
public class CombatDeterminismTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();

    /// <summary>
    /// Upgrades the human base (attack target #1), converts the AI base to a tower (attack target
    /// #2), has the AI attack the upgraded human base and fail against its raised defence, then has
    /// the human attack and capture the AI's tower with an overwhelming wave.
    /// </summary>
    private static void Play(Match match, Action<long> advance)
    {
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(human, 80);
        SetGarrison(ai, 50);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id))); // level 2, defence 110%
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, ai.Id, BaseType.Tower))); // level 1 tower, defence 140%

        // Attack #1: the AI's whole garrison (20, after the 30-unit conversion cost) against the
        // human's upgraded, well-stocked base - nowhere near enough under the new ratio formula.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, human.Id, ai.GarrisonCount)));
        advance(400); // long enough for the send to land and resolve

        Assert.Equal(match.HumanPlayer, human.Owner); // survived - the point of the attack

        // Attack #2 and the capture: the human throws its entire remaining garrison at the AI's
        // tower, which never produces and so still holds only what the conversion left it (20) -
        // an overwhelming enough wave to guarantee capture regardless of the exact production drift
        // during flight, which is what keeps this test from depending on a hand-computed constant.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, human.GarrisonCount)));
        advance(400);

        Assert.Equal(match.HumanPlayer, ai.Owner); // the capture really happened
        advance(250);
    }

    [Fact]
    public void SingleCall_AndIrregularChunks_AgreeOnGarrisonsOwnersLevelsTypesAndProgress()
    {
        var oneCall = new Match();
        Play(oneCall, oneCall.Advance);

        var chunked = new Match();
        Play(chunked, ticks => AdvanceInIrregularChunks(chunked, ticks));

        Assert.Equal(oneCall.ElapsedTicks, chunked.ElapsedTicks);
        Assert.Equal(Snapshot(oneCall), Snapshot(chunked));

        var capturedTower = oneCall.Bases.Single(b => b.Owner == oneCall.HumanPlayer && b.Type == BaseType.Tower);
        Assert.Equal(BaseType.Tower, capturedTower.Type); // capture kept the type
        Assert.Equal(LevelTable.MinLevel, capturedTower.Level); // was already level 1, floors there
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
