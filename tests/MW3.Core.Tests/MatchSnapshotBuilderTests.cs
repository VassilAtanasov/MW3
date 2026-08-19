namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-1: a match can be expressed as a snapshot. These cover what the byte-identical
/// <c>--dump-state</c> diff cannot reach - the builder's purity, the available-actions rule (the
/// dump only ever prints the actions of an open menu), and the fields no dump line carries.
/// Everything the dump does reach is covered by the 55 committed <c>qa/scripts/</c>, which is a
/// stronger standard than a test written by the session that decided what "complete" means (D-69).
/// </summary>
public class MatchSnapshotBuilderTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    [Fact]
    public void Snapshot_CarriesTheProtocolVersionMapAndTickOfTheMatchItWasBuiltFrom()
    {
        var match = new Match(MapCatalog.Medium);
        match.Advance(7);

        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(MatchSnapshot.CurrentProtocolVersion, snapshot.ProtocolVersion);
        Assert.Equal("Medium", snapshot.MapId);
        Assert.Equal(7, snapshot.ElapsedTicks);
        Assert.Equal(MatchOutcome.InProgress, snapshot.Outcome);
        Assert.Equal(match.HumanPlayer.Id, snapshot.LocalPlayerId);
        Assert.Equal(match.Obstacles, snapshot.Obstacles);
    }

    [Fact]
    public void Snapshot_BuiltFromACallerSuppliedLayout_NamesNoMap()
    {
        var match = new Match(new[]
        {
            new MapSlot(new MapPoint(0.2, 0.5), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.8, 0.5), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        });

        Assert.Null(MatchSnapshotBuilder.Build(match, match.HumanPlayer).MapId);
    }

    [Fact]
    public void Snapshot_NamesBothPlayersByIdAndCarriesEveryGlobalMultiplierTheyPlayUnder()
    {
        var match = new Match(MapCatalog.Big);

        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(2, snapshot.Players.Count);
        var human = snapshot.Players.Single(p => p.ControllerKind == PlayerControllerKind.Human);
        var ai = snapshot.Players.Single(p => p.ControllerKind == PlayerControllerKind.Ai);

        Assert.Equal(match.HumanPlayer.Id, human.Id);
        Assert.Equal(match.AiPlayer.Id, ai.Id);
        Assert.Equal(match.HumanMorale.Points, human.MoralePoints);
        Assert.Equal(match.HumanMorale.Level, human.MoraleLevel);
        Assert.Equal(MoraleTable.AttackPercentage(match.HumanMorale.Level), human.MoraleAttackPercentage);
        Assert.Equal(MoraleTable.DefencePercentage(match.HumanMorale.Level), human.MoraleDefencePercentage);
        Assert.Equal(match.ForgeCountFor(match.HumanPlayer), human.ForgeCount);
        Assert.Equal(ForgeTable.AttackPercentage(human.ForgeCount), human.ForgeAttackPercentage);
        Assert.Equal(ForgeTable.DefencePercentage(human.ForgeCount), human.ForgeDefencePercentage);

        Assert.Equal(human, snapshot.FindLocalPlayer());
    }

    [Fact]
    public void Snapshot_CarriesEveryBaseWithItsOwnerTypeLevelAndTheValuesATableWouldOtherwiseBeNeededFor()
    {
        var match = new Match(MapCatalog.Big);

        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(match.Bases.Count, snapshot.Bases.Count);
        for (var i = 0; i < match.Bases.Count; i++)
        {
            var source = match.Bases[i];
            var carried = snapshot.Bases[i];

            Assert.Equal(source.Id, carried.Id);
            Assert.Equal(source.Position, carried.Position);
            Assert.Equal(source.Owner?.Id, carried.OwnerPlayerId);
            Assert.Equal(source.Type, carried.Type);
            Assert.Equal(source.Level, carried.Level);
            Assert.Equal(source.GarrisonCount, carried.GarrisonCount);
            Assert.Equal(source.GarrisonCap, carried.GarrisonCap);
            Assert.Equal(source.DefencePercentage, carried.DefencePercentage);
            Assert.Equal(source.RingThicknessFractionOfRadius, carried.RingThicknessFractionOfRadius);
            Assert.Equal(source.MaxLevel, carried.MaxLevel);
            Assert.Equal(source.MaxUpgradableLevel, carried.MaxUpgradableLevel);
            Assert.Equal(source.ProductionProgressTicks, carried.ProductionProgressTicks);
            Assert.Equal(source.LastOwnerChangeTick, carried.LastOwnerChangeTick);
            Assert.Equal(source.OwnerBeforeLastChange?.Id, carried.OwnerBeforeLastChangePlayerId);
            Assert.Equal(source.LastFireTick, carried.LastFireTick);
            Assert.Null(carried.Construction);
        }

        // A neutral base has no owner id at all, rather than a sentinel one (D-11).
        Assert.Contains(snapshot.Bases, b => b.OwnerPlayerId is null);
    }

    [Fact]
    public void ABaseWithNoNextLevelToPrice_CarriesNoUpgradeCost()
    {
        // Big's centre slot starts a forge, which has one tier and no upgrade path at all - reading
        // Base.UpgradeCost for it throws, so the snapshot has to answer "none" rather than a number.
        var match = new Match(MapCatalog.Big);
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        var forge = snapshot.Bases.Single(b => b.Type == BaseType.Forge);
        Assert.Null(forge.UpgradeCost);

        var producer = snapshot.Bases.First(b => b.Type == BaseType.Producer);
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, producer.Level), producer.UpgradeCost);
    }

    [Fact]
    public void AnUpgradeInProgress_TravelsAsItsKindTargetLevelAndCompletionTick()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        SetGarrison(human, 60);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));

        var carried = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases.Single(b => b.Id == human.Id);
        var upgrade = Assert.IsType<PendingUpgrade>(human.Construction);

        Assert.NotNull(carried.Construction);
        Assert.Equal(BaseActionKind.Upgrade, carried.Construction!.Kind);
        Assert.Equal(upgrade.CompletionTick, carried.Construction.CompletionTick);
        Assert.Equal(upgrade.TargetLevel, carried.Construction.TargetLevel);
        Assert.Null(carried.Construction.TargetType);
    }

    [Fact]
    public void AConversionInProgress_TravelsAsItsKindTargetTypeAndCompletionTick()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        SetGarrison(human, 60);

        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Tower)));

        var carried = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases.Single(b => b.Id == human.Id);
        var conversion = Assert.IsType<PendingConversion>(human.Construction);

        Assert.NotNull(carried.Construction);
        Assert.Equal(BaseActionKind.Convert, carried.Construction!.Kind);
        Assert.Equal(conversion.CompletionTick, carried.Construction.CompletionTick);
        Assert.Equal(BaseType.Tower, carried.Construction.TargetType);
        Assert.Null(carried.Construction.TargetLevel);
    }

    [Fact]
    public void AvailableActions_AreCarriedForEveryBaseTheLocalPlayerOwnsInMatchOrder()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        SetGarrison(human, 60);

        var carried = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases.Single(b => b.Id == human.Id);
        var expected = match.AvailableActions(match.HumanPlayer, human.Id);

        Assert.Equal(expected.Count, carried.AvailableActions.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Kind, carried.AvailableActions[i].Kind);
            Assert.Equal(expected[i].Cost, carried.AvailableActions[i].Cost);
            Assert.Equal(expected[i].Availability, carried.AvailableActions[i].Availability);
            Assert.Equal(expected[i].ConvertTargetType, carried.AvailableActions[i].ConvertTargetType);
        }
    }

    [Fact]
    public void AvailableActions_AreCarriedForNoOtherBase()
    {
        var match = new Match(MapCatalog.Small);
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        foreach (var carried in snapshot.Bases.Where(b => b.OwnerPlayerId != match.HumanPlayer.Id))
        {
            Assert.Empty(carried.AvailableActions);
        }

        Assert.NotEmpty(snapshot.Bases.Single(b => b.OwnerPlayerId == match.HumanPlayer.Id).AvailableActions);
    }

    [Fact]
    public void ASnapshotBuiltForTheAi_CarriesTheAisActionsAndNotTheHumans()
    {
        var match = new Match(MapCatalog.Small);

        var snapshot = MatchSnapshotBuilder.Build(match, match.AiPlayer);

        Assert.Equal(match.AiPlayer.Id, snapshot.LocalPlayerId);
        Assert.NotEmpty(snapshot.Bases.Single(b => b.Id == AiBase(match).Id).AvailableActions);
        Assert.Empty(snapshot.Bases.Single(b => b.Id == HumanBase(match).Id).AvailableActions);
    }

    [Fact]
    public void BuildingASnapshot_MutatesNothing_SoTwoBuildsFromAnUnadvancedMatchAreEqual()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, AiBase(match).Id, 40)));
        match.Advance(12);

        var first = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var second = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void BuildingASnapshot_DoesNotChangeHowTheMatchGoesOn()
    {
        // Two matches driven identically, one of them snapshotted at every step. If building one
        // touched anything - a lazily-initialised field, a counter, a list order - the two would
        // come apart, and this is the property the server relies on to snapshot every single tick.
        var observed = new Match(MapCatalog.Medium);
        var control = new Match(MapCatalog.Medium);

        foreach (var match in new[] { observed, control })
        {
            SetGarrison(HumanBase(match), 40);
            Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, HumanBase(match).Id, AiBase(match).Id, 40)));
        }

        for (var step = 0; step < 40; step++)
        {
            MatchSnapshotBuilder.Build(observed, observed.HumanPlayer);
            observed.Advance(5);
            control.Advance(5);
        }

        Assert.Equal(
            MatchSnapshotBuilder.Build(control, control.HumanPlayer),
            MatchSnapshotBuilder.Build(observed, observed.HumanPlayer));
    }

    [Fact]
    public void AMultiWaveSend_CarriesEveryWaveAsItsOwnArmySharingOneSendId()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        SetGarrison(human, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, AiBase(match).Id, 40)));

        // Advance far enough for every wave to have launched, but not far enough for any to arrive.
        match.Advance(30);
        var armies = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Armies;

        Assert.Equal(match.ArmiesInFlight.Count, armies.Count);
        Assert.True(armies.Count > 1, "This send is meant to be several waves.");
        Assert.Single(armies.Select(a => a.SendId).Distinct());
        Assert.All(armies, a => Assert.Equal(armies.Count, a.WaveCount));
        Assert.Equal(Enumerable.Range(1, armies.Count), armies.Select(a => a.WaveIndex));

        for (var i = 0; i < armies.Count; i++)
        {
            var source = match.ArmiesInFlight[i];
            Assert.Equal(source.Id, armies[i].Id);
            Assert.Equal(source.Owner.Id, armies[i].OwnerPlayerId);
            Assert.Equal(source.SourceBaseId, armies[i].SourceBaseId);
            Assert.Equal(source.TargetBaseId, armies[i].TargetBaseId);
            Assert.Equal(source.UnitCount, armies[i].UnitCount);
            Assert.Equal(source.LaunchTick, armies[i].LaunchTick);
            Assert.Equal(source.ArrivalTick, armies[i].ArrivalTick);
        }
    }

    [Fact]
    public void ADetouredSend_CarriesItsWholeWaypointList_NotJustItsEndpoints()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        // Medium's centre obstacle sits between the two start bases, so this send has to detour.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.True(army.Path.Waypoints.Count > 2, "This send is meant to be detoured.");

        var carried = Assert.Single(MatchSnapshotBuilder.Build(match, match.HumanPlayer).Armies);
        Assert.Equal(army.Path.Waypoints, carried.PathWaypoints);
        Assert.Equal(army.Path.Length, carried.PathLength);
    }

    [Fact]
    public void ACarriedArmysPath_ResolvesToTheSamePositionTheMatchItselfDoes()
    {
        var match = new Match(MapCatalog.Medium);
        SetGarrison(HumanBase(match), 8);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, HumanBase(match).Id, AiBase(match).Id, 8)));
        match.Advance(25);

        var army = Assert.Single(match.ArmiesInFlight);
        var carried = Assert.Single(MatchSnapshotBuilder.Build(match, match.HumanPlayer).Armies);

        Assert.Equal(
            match.PositionOf(army),
            ArmyPathMath.PositionAt(carried.ToPath(), carried.LaunchTick, carried.ArrivalTick, match.ElapsedTicks));
        Assert.Equal(
            match.ProgressOf(army),
            ArmyPathMath.ProgressAt(carried.LaunchTick, carried.ArrivalTick, match.ElapsedTicks));
    }

    [Fact]
    public void ADecidedMatch_CarriesItsOutcome()
    {
        var match = new Match(MapCatalog.Small);
        typeof(Match).GetProperty(nameof(Match.Outcome))!.GetSetMethod(nonPublic: true)!
            .Invoke(match, new object?[] { MatchOutcome.HumanVictory });

        Assert.Equal(MatchOutcome.HumanVictory, MatchSnapshotBuilder.Build(match, match.HumanPlayer).Outcome);
    }

    [Fact]
    public void Build_RejectsANullMatchOrPlayer()
    {
        var match = new Match(MapCatalog.Small);

        Assert.Throws<ArgumentNullException>(() => MatchSnapshotBuilder.Build(null!, match.HumanPlayer));
        Assert.Throws<ArgumentNullException>(() => MatchSnapshotBuilder.Build(match, null!));
    }
}
