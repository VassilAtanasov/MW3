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
        using var gateway = new TestMatchGateway(match);
        var menu = new BaseActionMenu(gateway, humanBase.Id);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.NotNull(humanBase.Construction);

        // Force the garrison back to exactly what it was when the menu last cached it, so the only
        // thing that has actually changed is Construction - the scenario issue #46 warns about.
        SetGarrison(humanBase, startingGarrison);
        Assert.Equal(startingGarrison, humanBase.GarrisonCount);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);

        gateway.Refresh();
        menu.Refresh();

        var expected = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases.Single(b => b.Id == humanBase.Id).AvailableActions;
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
        using var gateway = new TestMatchGateway(match);
        var menu = new BaseActionMenu(gateway, humanBase.Id);

        // Already at MinLevel, so the conversion's level reset to MinLevel (D-30) is a no-op -
        // exactly the "Level and GarrisonCount both unchanged" case the issue describes.
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(BaseType.Tower, humanBase.Type);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        Assert.Null(humanBase.Construction);

        SetGarrison(humanBase, startingGarrison);

        gateway.Refresh();
        menu.Refresh();

        var expected = MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases.Single(b => b.Id == humanBase.Id).AvailableActions;
        Assert.Equal(expected, menu.Actions);
        Assert.Equal(LevelTable.UpgradeCost(BaseType.Tower, LevelTable.MinLevel), menu.Actions[0].Cost);
    }

    // Independent per-button clamping (the layout before FR-5) let two buttons overlap whenever
    // their raw arc positions were closer together than a button's own width - true at every anchor,
    // not only one near an edge, since the chord distance between two arc points at a fixed radius
    // and angular step never depends on the anchor's position at all. Exercised at both target
    // viewports, anchored at the human base's real starting position (0.12, 0.50, near the left
    // edge - MapLayout) where the overlap this regression test guards against was first found.
    // Now three buttons (Upgrade, Convert:Tower, Convert:Forge) once BaseType gained Forge (D-48) -
    // every pair is checked, not only the first two.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void ThreeButtons_NeverOverlap(int width, int height)
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        SetGarrison(humanBase, LevelTable.ConversionCost + 10); // both converts affordable too, so every button renders live
        using var gateway = new TestMatchGateway(match);
        var menu = new BaseActionMenu(gateway, humanBase.Id);
        Assert.Equal(3, menu.Actions.Count);

        var viewport = new Viewport(0, 0, width, height);

        for (var i = 0; i < menu.ButtonCount; i++)
        {
            for (var j = i + 1; j < menu.ButtonCount; j++)
            {
                var rectI = GetButtonRect(menu, i, viewport);
                var rectJ = GetButtonRect(menu, j, viewport);
                Assert.False(rectI.Intersects(rectJ), $"buttons {i} and {j} overlap at {width}x{height}: {rectI} vs {rectJ}");
            }
        }
    }
}
