namespace MW3.Core.Tests;

public class ConvertTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void Convert_SubtractsTheCostImmediately_ButOnlySetsTheTypeOnCompletion()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetLevel(humanBase, 3);
        SetGarrison(humanBase, 40);

        var outcome = match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower));

        Assert.Equal(ConvertOutcome.Accepted, outcome);
        Assert.Equal(10, humanBase.GarrisonCount); // paid immediately
        Assert.Equal(BaseType.Producer, humanBase.Type); // still a producer - the type change is delayed (D-30)
        Assert.Equal(3, humanBase.Level); // level reset is delayed too
        Assert.NotNull(humanBase.Construction);

        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(BaseType.Tower, humanBase.Type);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Null(humanBase.Construction);
    }

    [Fact]
    public void Convert_BackToProducer_ResetsLevelAndZeroesProgress_OnlyOnCompletion()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 70); // leaves room below the level-1 cap after both conversion costs
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
        SetLevel(humanBase, 3);

        var outcome = match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Producer));
        Assert.Equal(ConvertOutcome.Accepted, outcome);
        Assert.Equal(BaseType.Tower, humanBase.Type); // still a tower while this conversion builds
        Assert.Equal(3, humanBase.Level);

        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(BaseType.Producer, humanBase.Type);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        // A fresh period, not progress inherited from before it was a tower: one tick short of the
        // level-1 period produces nothing, the next tick produces exactly one unit.
        var before = humanBase.GarrisonCount;
        match.Advance(LevelTable.Village.ProductionPeriodTicks(LevelTable.MinLevel) - 1);
        Assert.Equal(before, humanBase.GarrisonCount);
        match.Advance(1);
        Assert.Equal(before + 1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Tower_NeverProduces_GarrisonUnchangedAfter1000TicksAtAnyLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
        SetLevel(humanBase, 3);
        var garrison = humanBase.GarrisonCount;

        match.Advance(1000);

        Assert.Equal(garrison, humanBase.GarrisonCount);
    }

    [Fact]
    public void Tower_ProductionProgressIsZero_AtEveryTick_NotMerelyFrozenAtAValue()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);

        for (var i = 0; i < 5; i++)
        {
            match.Advance(37); // an arbitrary, non-period-aligned span
            Assert.Equal(0, humanBase.ProductionProgressTicks);
        }
    }

    [Fact]
    public void Tower_HasNoGarrisonCap_ArrivalsAlwaysAddInFull_WithNothingProduced()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Capture the neutral base first, for a second human base to reinforce from.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        SetGarrison(humanBase, 38);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        Assert.Equal(8, humanBase.GarrisonCount);
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
        Assert.Null(humanBase.GarrisonCap); // a tower has no cap at all - not even a very high one

        var beforeReinforce = humanBase.GarrisonCount;
        SetGarrison(neutral, 20);
        // 8 units - the largest send that stays a single wave (FR-3) - reinforce in one arrival.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 8)));
        AdvanceToNextArrival(match);

        Assert.Equal(beforeReinforce + 8, humanBase.GarrisonCount); // all 8 arrived - there is no cap to clamp against

        var afterReinforce = humanBase.GarrisonCount;
        match.Advance(1000);
        Assert.Equal(afterReinforce, humanBase.GarrisonCount); // still a tower: nothing produced, nothing destroyed
    }

    [Fact]
    public void Tower_CanSendArmies_ExactlyAsAProducerCan_WithNoTowerBranchInTheSendPath()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(humanBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
        var garrisonBeforeSend = humanBase.GarrisonCount;

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 5));

        Assert.Equal(SendArmyOutcome.Accepted, outcome);
        Assert.Equal(garrisonBeforeSend - 5, humanBase.GarrisonCount);
    }

    [Fact]
    public void Tower_CanBeUpgraded_LevelRisesButProductionStaysZero()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 60);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
        var afterConversion = humanBase.GarrisonCount;

        var outcome = match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id));
        Assert.Equal(UpgradeOutcome.Accepted, outcome);
        Assert.Equal(afterConversion - LevelTable.UpgradeCost(BaseType.Tower, LevelTable.MinLevel), humanBase.GarrisonCount);

        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);

        var afterUpgrade = humanBase.GarrisonCount;
        match.Advance(1000);
        Assert.Equal(afterUpgrade, humanBase.GarrisonCount); // the higher level still buys nothing observable this feature
    }

    [Fact]
    public void Tower_CanBeCaptured_ButDefendsAtItsLevelsDefencePercentage()
    {
        // Superseded by D-29 (FR-3b): a level-1 tower defends at 140%, not phase 2's plain 1:1.
        // The wave still falls it, but the survivor count comes from the ratio formula, not N - M.
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        SetGarrison(aiBase, 35);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, aiBase.Type);
        // A level-1 tower's own fire hits its incoming attacker on the way in (FR-4), so the
        // defending garrison is set to 0 here rather than a token few: with the tower stripping
        // most of an 8-unit wave - the largest that stays a single wave (FR-3) - down to a handful
        // of survivors, any nonzero defender at 140% would be enough to repel what is left.
        SetGarrison(aiBase, 0);

        SetGarrison(humanBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 8)));
        var army = match.ArmiesInFlight.Single(); // 8 units or fewer never splits into waves (FR-3)
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var survivingAttackers = army.UnitCount;
        Assert.True(survivingAttackers < 8, "the tower should have shot down at least one unit before arrival");
        match.Advance(1);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal((survivingAttackers * 100) / 140, aiBase.GarrisonCount); // Bu = (a/d) x Wu, a=100, d=140
        Assert.Equal(BaseType.Tower, aiBase.Type); // capture keeps the type
        Assert.Equal(LevelTable.MinLevel, aiBase.Level); // was already level 1, floors there
    }

    [Fact]
    public void Capture_OfATowerAboveMinLevel_KeepsTheTypeAndDropsExactlyOneLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        SetGarrison(aiBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, aiBase.Type);
        SetLevel(aiBase, 3);
        // A level-3 tower's own fire hits its incoming attacker on the way in (FR-4), so the
        // defending garrison is set to 0 here rather than a token 1: with the tower stripping most
        // of an 8-unit wave - the largest that stays a single wave (FR-3) - down to a handful of
        // survivors, any nonzero defender at 190% would be enough to repel what is left.
        SetGarrison(aiBase, 0);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 8)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(BaseType.Tower, aiBase.Type); // still a tower, one level lower
        Assert.Equal(2, aiBase.Level);
        Assert.Equal(0, aiBase.ProductionProgressTicks); // still produces nothing for the new owner
    }

    [Fact]
    public void Capture_OfAProducer_KeepsItAProducer()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(BaseType.Producer, aiBase.Type);
    }

    [Fact]
    public void Convert_DownToExactlyZeroGarrison_IsLegal()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost); // exactly the conversion cost
        Assert.Equal(LevelTable.ConversionCost, humanBase.GarrisonCount);

        var outcome = match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower));

        Assert.Equal(ConvertOutcome.Accepted, outcome);
        Assert.Equal(0, humanBase.GarrisonCount);
        Assert.Equal(match.HumanPlayer, humanBase.Owner); // still owned at zero
    }

    [Fact]
    public void Convert_ProducerAtOrAboveItsCap_IsLegal_AndTheResultingTowerKeepsThatGarrison()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 35); // above the level-1 cap of 20

        var outcome = match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower));

        Assert.Equal(ConvertOutcome.Accepted, outcome);
        Assert.Equal(5, humanBase.GarrisonCount); // 35 - 30, nothing else destroyed
    }

    [Fact]
    public void PlayerHoldingOnlyTowers_IsNotEliminated_AndSimplyCannotProduce()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 35);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));

        match.Advance(2000);

        Assert.Equal(MatchOutcome.InProgress, match.Outcome);
        Assert.Equal(match.HumanPlayer, humanBase.Owner);
        Assert.Equal(BaseType.Tower, humanBase.Type);

        // 35 - 30 conversion cost = 5, plus exactly one unit produced at the still-current level-1
        // rate during the 100-tick build itself (D-30) - then nothing at all for the remaining 1900
        // ticks once it is a tower.
        Assert.Equal(6, humanBase.GarrisonCount);
    }

    [Fact]
    public void Convert_UnknownBaseId_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var before = Snapshot(match);

        Assert.Equal(ConvertOutcome.BaseNotFound, match.Execute(new ConvertCommand(match.HumanPlayer, 99, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_BaseOwnedByTheOtherPlayer_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var before = Snapshot(match);

        Assert.Equal(
            ConvertOutcome.BaseNotOwnedByIssuer,
            match.Execute(new ConvertCommand(match.HumanPlayer, AiBase(match).Id, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_NeutralBase_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);
        var before = Snapshot(match);

        Assert.Equal(
            ConvertOutcome.BaseNotOwnedByIssuer,
            match.Execute(new ConvertCommand(match.HumanPlayer, neutral.Id, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_AlreadyOfTheTargetType_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var before = Snapshot(match);

        Assert.Equal(
            ConvertOutcome.AlreadyOfTargetType,
            match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Producer)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_AlreadyUnderConstruction_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        var before = Snapshot(match);

        Assert.Equal(
            ConvertOutcome.UnderConstruction,
            match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_GarrisonBelowCost_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Spend down to 5, below the conversion cost of 30.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 5)));
        Assert.Equal(5, humanBase.GarrisonCount);
        var before = Snapshot(match);

        Assert.Equal(
            ConvertOutcome.GarrisonBelowCost,
            match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_OnceTheMatchIsDecided_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        SetGarrison(aiBase, 1);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        match.Advance(200);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        var before = Snapshot(match);
        Assert.Equal(
            ConvertOutcome.MatchAlreadyDecided,
            match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Convert_NullCommand_Throws()
    {
        var match = new Match();

        Assert.Throws<ArgumentNullException>(() => match.Execute((ConvertCommand)null!));
    }

    [Fact]
    public void Convert_NullIssuingPlayer_Throws_RatherThanMatchingANeutralBasesAbsentOwner()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Throws<ArgumentException>(() => match.Execute(new ConvertCommand(null!, neutral.Id, BaseType.Tower)));

        Assert.Null(neutral.Owner);
        Assert.Equal(BaseType.Producer, neutral.Type);
    }

    [Fact]
    public void MatchRunner_SubmitsConverts_ThroughTheSameSinglePath()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost);

        var outcome = runner.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower));
        Assert.Equal(ConvertOutcome.Accepted, outcome);

        runner.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);
    }

    [Fact]
    public void Convert_OnATower_OffersUpgradeAndConvertBackToProducer_FR5()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, humanBase.Type);

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(3, actions.Count);
        Assert.Equal(BaseActionKind.Upgrade, actions[0].Kind);

        // A tower's other two types, in BaseType declaration order (D-48): Producer then Forge.
        var convertToProducer = actions[1];
        Assert.Equal(BaseActionKind.Convert, convertToProducer.Kind);
        Assert.Equal(BaseType.Producer, convertToProducer.ConvertTargetType);

        var convertToForge = actions[2];
        Assert.Equal(BaseActionKind.Convert, convertToForge.Kind);
        Assert.Equal(BaseType.Forge, convertToForge.ConvertTargetType);
    }

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
    }

    private static (int Id, Player? Owner, BaseType Type, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.Type, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();
}
