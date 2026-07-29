using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game.Tests;

public class BaseActionMenuTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static Rectangle GetButtonRect(BaseActionMenu menu, int index, Viewport viewport) =>
        (Rectangle)typeof(BaseActionMenu)
            .GetMethod("GetButtonRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(menu, new object[] { index, viewport })!;

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

    // Independent per-button clamping (the layout before FR-5) let two buttons overlap whenever
    // their raw arc positions were closer together than a button's own width - true at every anchor,
    // not only one near an edge, since the chord distance between two arc points at a fixed radius
    // and angular step never depends on the anchor's position at all. Exercised at both target
    // viewports, anchored at the human base's real starting position (0.12, 0.50, near the left
    // edge - MapLayout) where the overlap this regression test guards against was first found.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void TwoButtons_NeverOverlap(int width, int height)
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost + 10); // Convert affordable too, so both buttons render live
        var menu = new BaseActionMenu(match, match.HumanPlayer, humanBase.Id);
        Assert.Equal(2, menu.Actions.Count);

        var viewport = new Viewport(0, 0, width, height);

        var rect0 = GetButtonRect(menu, 0, viewport);
        var rect1 = GetButtonRect(menu, 1, viewport);

        Assert.False(rect0.Intersects(rect1), $"button rects overlap at {width}x{height}: {rect0} vs {rect1}");
    }
}
