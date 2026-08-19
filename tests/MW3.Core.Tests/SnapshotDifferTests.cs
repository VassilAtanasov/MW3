namespace MW3.Core.Tests;

public class SnapshotDifferTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void Diff_OfASnapshotAgainstItself_IsEmpty()
    {
        var match = new Match();
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(snapshot, snapshot);

        Assert.Empty(batch.Events);
        Assert.Equal(snapshot.ElapsedTicks, batch.FromTick);
        Assert.Equal(snapshot.ElapsedTicks, batch.ToTick);
    }

    [Fact]
    public void Diff_CarriesTheFromAndToTicksOfTheSnapshotsItWasBuiltFrom()
    {
        var match = new Match();
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        match.Advance(140);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);

        Assert.Equal(a.ElapsedTicks, batch.FromTick);
        Assert.Equal(b.ElapsedTicks, batch.ToTick);
    }

    [Fact]
    public void Diff_IsDeterministic_ProducingAByteIdenticalBatchOnRepeatedCalls()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        SetGarrison(human, 40);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 30)));
        match.Advance(200);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var first = SnapshotDiffer.Diff(a, b);
        var second = SnapshotDiffer.Diff(a, b);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Diff_WorksOnNonAdjacentSnapshots_JustAsOnAdjacentOnes()
    {
        var match = new Match();
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        match.Advance(437);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);

        Assert.Equal(437, batch.ToTick - batch.FromTick);
        Assert.Equal(b, SnapshotApplier.Apply(batch, a));
    }

    [Fact]
    public void Diff_OnDifferentMaps_ThrowsNamingBothMapIds()
    {
        var smallMatch = new Match(MapCatalog.Small);
        var smallSnapshot = MatchSnapshotBuilder.Build(smallMatch, smallMatch.HumanPlayer);
        var bigMatch = new Match(MapCatalog.Big);
        var bigSnapshot = MatchSnapshotBuilder.Build(bigMatch, bigMatch.HumanPlayer);

        var ex = Assert.Throws<InvalidOperationException>(() => SnapshotDiffer.Diff(smallSnapshot, bigSnapshot));

        Assert.Contains("Small", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Big", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseThatChangesOwner_EmitsOnlyBaseCaptured_NeverAlsoBaseChanged()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 9)));
        match.Advance(2000);
        Assert.Equal(match.HumanPlayer, neutral.Owner);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);

        var eventsForNeutral = batch.Events.Where(e => e.BaseId == neutral.Id).ToList();
        Assert.Single(eventsForNeutral);
        Assert.Equal(MatchEventKind.BaseCaptured, eventsForNeutral[0].Kind);
    }

    [Fact]
    public void ConstructionStartingAndCompleting_EmitTheirOwnEventKinds()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        SetGarrison(human, 60);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var startBatch = SnapshotDiffer.Diff(a, b);
        Assert.Contains(startBatch.Events, e => e.BaseId == human.Id && e.Kind == MatchEventKind.ConstructionStarted);

        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        var c = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var completeBatch = SnapshotDiffer.Diff(b, c);
        Assert.Contains(completeBatch.Events, e => e.BaseId == human.Id && e.Kind == MatchEventKind.ConstructionCompleted);
    }

    [Fact]
    public void AnArmyThatArrivesOrIsDestroyed_EmitsArmyRemoved_CarryingItsLastKnownUnitCount()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 9)));
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var launchedUnitCount = a.Armies.Single().UnitCount;

        match.Advance(2000);
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        Assert.Empty(b.Armies);

        var batch = SnapshotDiffer.Diff(a, b);

        var removed = Assert.Single(batch.Events, e => e.Kind == MatchEventKind.ArmyRemoved);
        Assert.Equal(launchedUnitCount, removed.LastKnownUnitCount);
        Assert.Null(removed.Army);
    }

    [Fact]
    public void ANewArmy_EmitsArmyLaunched_CarryingItsFullLaunchData()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(x => x.Owner is null);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 9)));
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);

        var launched = Assert.Single(batch.Events, e => e.Kind == MatchEventKind.ArmyLaunched);
        Assert.Equal(b.Armies.Single(), launched.Army);
    }

    [Fact]
    public void EventsAreOrdered_BasesAscendingThenArmiesAscendingThenMatchLevel()
    {
        var match = new Match();
        var human = match.Bases.Single(x => x.Owner == match.HumanPlayer);
        var neutral2 = match.Bases[3];
        var neutral3 = match.Bases[2];
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        SetGarrison(human, 60);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral2.Id, 9)));
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral3.Id, 9)));
        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var batch = SnapshotDiffer.Diff(a, b);

        var baseIds = batch.Events.Where(e => e.BaseId.HasValue).Select(e => e.BaseId!.Value).ToList();
        Assert.Equal(baseIds.OrderBy(x => x), baseIds);

        var armyIds = batch.Events.Where(e => e.ArmyId.HasValue).Select(e => e.ArmyId!.Value).ToList();
        Assert.Equal(armyIds.OrderBy(x => x), armyIds);

        var eventList = batch.Events.ToList();
        var lastBaseIndex = eventList.FindLastIndex(e => e.BaseId.HasValue);
        var firstArmyIndex = eventList.FindIndex(e => e.ArmyId.HasValue);
        if (lastBaseIndex >= 0 && firstArmyIndex >= 0)
        {
            Assert.True(lastBaseIndex < firstArmyIndex);
        }
    }
}
