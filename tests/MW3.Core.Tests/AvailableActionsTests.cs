namespace MW3.Core.Tests;

public class AvailableActionsTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    [Fact]
    public void OwnedBase_BelowMaxLevel_Affordable_ReturnsUpgradeAndConvert_UpgradeAffordable()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);

        Assert.Equal(2, actions.Count);
        var upgrade = actions[0];
        Assert.Equal(BaseActionKind.Upgrade, upgrade.Kind);
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), upgrade.Cost);
        Assert.Equal(BaseActionAvailability.Affordable, upgrade.Availability);

        var convert = actions[1];
        Assert.Equal(BaseActionKind.Convert, convert.Kind);
        Assert.Equal(LevelTable.ConversionCost, convert.Cost);
        Assert.Equal(BaseType.Tower, convert.ConvertTargetType);
        // Starting garrison (10) is below the 30-unit conversion cost.
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, convert.Availability);
    }

    [Fact]
    public void OwnedBase_GarrisonBelowCost_ReturnsGreyedUpgrade_StillShowingItsCost()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Spend down to 4, below the level-1 upgrade cost of 5.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(2, actions.Count);
        var upgrade = actions[0];
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), upgrade.Cost);
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, upgrade.Availability);
    }

    [Fact]
    public void OwnedBase_AtMaxLevel_ReturnsUpgradeWithZeroCost_ReadingMax_ConvertStillIndependentlyLive()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        // Reaching the upgradable ceiling (level 4) takes three completed upgrades - costs 5, 10, 20,
        // each with its own build - not two: the village ladder's level 5 exists but has no published
        // upgrade price (MaxUpgradableLevel).
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(1) + 500);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(2) + 500);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(3) + 500);
        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(2, actions.Count);
        var upgrade = actions[0];
        Assert.Equal(0, upgrade.Cost);
        Assert.Equal(BaseActionAvailability.AlreadyAtMaxLevel, upgrade.Availability);

        // Convert is not gated by the upgrade ladder at all - it is independently affordable now
        // that production during three builds has accumulated well past the 30-unit conversion cost.
        var convert = actions[1];
        Assert.Equal(BaseActionKind.Convert, convert.Kind);
        Assert.NotEqual(BaseActionAvailability.AlreadyAtMaxLevel, convert.Availability);
    }

    [Fact]
    public void OwnedBase_UnderConstruction_ReturnsGreyedUpgrade_AndGreyedConvert_BothUnderConstruction()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(2, actions.Count);
        var upgrade = actions[0];
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), upgrade.Cost); // still reads the current (unchanged) level
        Assert.Equal(BaseActionAvailability.UnderConstruction, upgrade.Availability);

        var convert = actions[1];
        Assert.Equal(BaseActionAvailability.UnderConstruction, convert.Availability);
        Assert.Equal(BaseType.Tower, convert.ConvertTargetType);

        // Completing leaves the base at level 2 with 6 units (10 - 5 cost + 1 produced during the
        // build at the still-current level-1 rate) - below level 2's 10-unit cost, so the menu
        // greys it for a different reason now, not affordable outright.
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        var afterCompletion = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, afterCompletion[0].Availability);
    }

    [Fact]
    public void BaseOwnedByTheOtherPlayer_ReturnsNothing()
    {
        var match = new Match();

        Assert.Empty(match.AvailableActions(match.HumanPlayer, AiBase(match).Id));
    }

    [Fact]
    public void NeutralBase_ReturnsNothing()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Empty(match.AvailableActions(match.HumanPlayer, neutral.Id));
    }

    [Fact]
    public void UnknownBaseId_ReturnsNothing()
    {
        var match = new Match();

        Assert.Empty(match.AvailableActions(match.HumanPlayer, 99));
    }

    [Fact]
    public void NullPlayer_Throws()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Throws<ArgumentNullException>(() => match.AvailableActions(null!, humanBase.Id));
    }

    [Fact]
    public void ReflectsLiveState_AsGarrisonCrossesTheCost()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Equal(BaseActionAvailability.Affordable, match.AvailableActions(match.HumanPlayer, humanBase.Id)[0].Availability);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, match.AvailableActions(match.HumanPlayer, humanBase.Id)[0].Availability);

        match.Advance(LevelTable.Village.ProductionPeriodTicks(LevelTable.MinLevel) * 3);
        Assert.Equal(BaseActionAvailability.Affordable, match.AvailableActions(match.HumanPlayer, humanBase.Id)[0].Availability);
    }

    [Fact]
    public void Convert_TargetType_IsOppositeOfCurrentType()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(BaseType.Producer, humanBase.Type);
        var convert = match.AvailableActions(match.HumanPlayer, humanBase.Id)[1];
        Assert.Equal(BaseType.Tower, convert.ConvertTargetType);
    }

    [Fact]
    public void Convert_CostIsAlwaysLevelTableConversionCost_RegardlessOfLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(1) + 500);

        var convert = match.AvailableActions(match.HumanPlayer, humanBase.Id)[1];
        Assert.Equal(LevelTable.ConversionCost, convert.Cost);
    }
}
