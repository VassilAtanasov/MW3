namespace MW3.Core.Tests;

public class SnapshotApplierTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void Apply_OfAnEmptyBatch_ReturnsASnapshotEqualToTheOriginal()
    {
        var match = new Match();
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var empty = SnapshotDiffer.Diff(snapshot, snapshot);

        var result = SnapshotApplier.Apply(empty, snapshot);

        Assert.Equal(snapshot, result);
    }

    [Fact]
    public void Apply_WhenTheBatchsFromTickDoesNotMatchTheSnapshot_ThrowsNamingBoth()
    {
        var match = new Match();
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        match.Advance(50);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(a, b);

        match.Advance(50);
        var wrongBase = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var ex = Assert.Throws<InvalidOperationException>(() => SnapshotApplier.Apply(batch, wrongBase));

        Assert.Contains(batch.FromTick.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
        Assert.Contains(wrongBase.ElapsedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_MutatesNeitherArgument()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 9)));
        match.Advance(2000);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(a, b);

        var aBasesBefore = a.Bases.ToList();
        var aArmiesBefore = a.Armies.ToList();

        var result = SnapshotApplier.Apply(batch, a);

        Assert.Equal(aBasesBefore, a.Bases);
        Assert.Equal(aArmiesBefore, a.Armies);
        Assert.NotSame(a, result);
    }

    [Fact]
    public void Apply_ResultsElapsedTicks_EqualTheBatchsToTick()
    {
        var match = new Match();
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        match.Advance(311);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(a, b);

        var result = SnapshotApplier.Apply(batch, a);

        Assert.Equal(batch.ToTick, result.ElapsedTicks);
    }

    [Fact]
    public void Apply_AfterAnUpgradeCyclesAndASend_ReproducesTheLaterSnapshotExactly()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        SetGarrison(human, 60);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 9)));
        match.Advance(300);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);
        var result = SnapshotApplier.Apply(batch, a);

        Assert.Equal(b, result);
    }
}
