using MW3.Core;

namespace MW3.Game.Tests;

/// <summary>
/// Phase 6 FR-5: each convert button now carries its target type in its label - <c>Producer: 30</c>,
/// <c>Tower: 30</c>, <c>Forge: 30</c> - rather than the identical <c>Convert: 30</c> every convert
/// button carried before this feature, so a player can tell two convert buttons apart before
/// pressing one. See issue #89's acceptance criteria.
/// </summary>
public class BaseActionMenuLabelTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static string[] GetLabels(BaseActionMenu menu) =>
        (string[])typeof(BaseActionMenu).GetField("_labels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(menu)!;

    [Fact]
    public void ConvertButtons_AreLabelledWithTheirTargetTypeAndCost_NeverTheBareWordConvert()
    {
        var match = new Match();
        var human = HumanBase(match);
        var menu = new BaseActionMenu(match, match.HumanPlayer, human.Id);

        var actions = menu.Actions;
        var labels = GetLabels(menu);

        Assert.Equal(BaseActionKind.Upgrade, actions[0].Kind);
        Assert.StartsWith("Upgrade:", labels[0]);

        for (var i = 1; i < actions.Count; i++)
        {
            Assert.Equal(BaseActionKind.Convert, actions[i].Kind);
            var targetType = actions[i].ConvertTargetType!.Value;
            Assert.Equal(FormattableString.Invariant($"{targetType}: {actions[i].Cost}"), labels[i]);
            Assert.DoesNotContain("Convert:", labels[i]);
        }
    }

    /// <summary>
    /// The defect this feature fixes, stated directly: with three base types a base always shows two
    /// convert buttons, and before this change both read the identical <c>Convert: 30</c>. Now no two
    /// buttons on one menu ever carry the same text.
    /// </summary>
    [Fact]
    public void NoTwoButtonsOnOneMenu_EverCarryTheSameText()
    {
        var match = new Match();
        var human = HumanBase(match);
        var menu = new BaseActionMenu(match, match.HumanPlayer, human.Id);

        var labels = GetLabels(menu);

        Assert.Equal(3, labels.Length); // Upgrade, Convert:Tower, Convert:Forge (D-48 order)
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    [Fact]
    public void AConvertButtonUnderConstruction_ReadsItsTargetTypeThenBuilding()
    {
        var match = new Match();
        var human = HumanBase(match);
        SetGarrison(human, LevelTable.ConversionCost + 10);
        var menu = new BaseActionMenu(match, match.HumanPlayer, human.Id);

        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Tower)));
        menu.Refresh();

        var labels = GetLabels(menu);
        var towerButtonIndex = Array.FindIndex(menu.Actions.ToArray(), a => a.ConvertTargetType == BaseType.Tower);

        Assert.Equal("Tower: Building", labels[towerButtonIndex]);
    }

    /// <summary>The upgrade button's three label forms are untouched - only convert labels changed.</summary>
    [Theory]
    [InlineData(0, "Upgrade: 5")] // level 1, affordable
    public void UpgradeLabel_IsByteIdenticalToBeforeThisFeature(int _, string expected)
    {
        var match = new Match();
        var human = HumanBase(match);
        var menu = new BaseActionMenu(match, match.HumanPlayer, human.Id);

        var labels = GetLabels(menu);

        Assert.Equal(expected, labels[0]);
    }

    [Fact]
    public void UpgradeLabel_AtMaxLevel_ReadsMax()
    {
        var match = new Match();
        var human = HumanBase(match);
        SetGarrison(human, 999);
        var menu = new BaseActionMenu(match, match.HumanPlayer, human.Id);

        for (var level = LevelTable.MinLevel; level < LevelTable.Village.MaxUpgradableLevel; level++)
        {
            Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));
            match.Advance(LevelTable.UpgradeBuildDurationTicks(level));
        }

        menu.Refresh();
        var labels = GetLabels(menu);

        Assert.Equal("Upgrade: Max", labels[0]);
    }
}
