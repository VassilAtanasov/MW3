using MW3.Core;

namespace MW3.Game.Tests;

public class BaseActionMenuTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void Refresh_ReQueriesWhenConstructionStarts_EvenIfGarrisonAndLevelHappenToMatchTheCache()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var startingGarrison = humanBase.GarrisonCount;
        var menu = new BaseActionMenu(match, match.HumanPlayer, humanBase.Id);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.NotNull(humanBase.Construction);

        // Force the garrison back to exactly what it was when the menu last cached it, so the only
        // thing that has actually changed is Construction - the scenario issue #46 warns about.
        SetGarrison(humanBase, startingGarrison);
        Assert.Equal(startingGarrison, humanBase.GarrisonCount);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);

        menu.Refresh();

        var expected = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(expected, menu.Actions);
        Assert.Equal(BaseActionAvailability.UnderConstruction, menu.Actions[0].Availability);
    }

    [Fact]
    public void Refresh_ReQueriesWhenTypeChanges_EvenIfGarrisonAndLevelHappenToMatchTheCache()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost + 10); // enough to afford the conversion below
        var startingGarrison = humanBase.GarrisonCount;
        var menu = new BaseActionMenu(match, match.HumanPlayer, humanBase.Id);

        // Already at MinLevel, so the conversion's level reset to MinLevel (D-30) is a no-op -
        // exactly the "Level and GarrisonCount both unchanged" case the issue describes.
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(BaseType.Tower, humanBase.Type);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Null(humanBase.Construction);

        SetGarrison(humanBase, startingGarrison);

        menu.Refresh();

        var expected = match.AvailableActions(match.HumanPlayer, humanBase.Id);
        Assert.Equal(expected, menu.Actions);
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Tower, LevelTable.MinLevel), menu.Actions[0].Cost);
    }
}
