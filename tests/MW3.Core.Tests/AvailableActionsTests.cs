namespace MW3.Core.Tests;

public class AvailableActionsTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    [Fact]
    public void OwnedBase_BelowMaxLevel_Affordable_ReturnsExactlyOneUpgradeAction()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        var actions = match.AvailableActions(match.HumanPlayer, humanBase.Id);

        var action = Assert.Single(actions);
        Assert.Equal(BaseActionKind.Upgrade, action.Kind);
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), action.Cost);
        Assert.Equal(BaseActionAvailability.Affordable, action.Availability);
    }

    [Fact]
    public void OwnedBase_GarrisonBelowCost_ReturnsGreyedUpgrade_StillShowingItsCost()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Spend down to 4, below the level-1 upgrade cost of 5.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));

        var action = Assert.Single(match.AvailableActions(match.HumanPlayer, humanBase.Id));
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), action.Cost);
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, action.Availability);
    }

    [Fact]
    public void OwnedBase_AtMaxLevel_ReturnsUpgradeWithZeroCost_ReadingMax()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        // Reaching the upgradable ceiling (level 4) takes three upgrades - costs 5, 10, 20 - not two:
        // the village ladder's level 5 exists but has no published upgrade price (MaxUpgradableLevel).
        match.Advance(60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(200);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(400);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);

        var action = Assert.Single(match.AvailableActions(match.HumanPlayer, humanBase.Id));
        Assert.Equal(0, action.Cost);
        Assert.Equal(BaseActionAvailability.AlreadyAtMaxLevel, action.Availability);
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

        Assert.Equal(BaseActionAvailability.Affordable, Assert.Single(match.AvailableActions(match.HumanPlayer, humanBase.Id)).Availability);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));
        Assert.Equal(BaseActionAvailability.GarrisonBelowCost, Assert.Single(match.AvailableActions(match.HumanPlayer, humanBase.Id)).Availability);

        match.Advance(LevelTable.Village.ProductionPeriodTicks(LevelTable.MinLevel) * 3);
        Assert.Equal(BaseActionAvailability.Affordable, Assert.Single(match.AvailableActions(match.HumanPlayer, humanBase.Id)).Availability);
    }
}
