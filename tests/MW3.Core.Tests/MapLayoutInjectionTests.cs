namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-1, D-44: the map layout is a value <see cref="Match"/> accepts. This is the seam that
/// makes a neutral forge (and later, FR-2's contested one) testable before the shipped map itself
/// changes. FR-2 (phase 7) retired <c>MapLayout</c> and made the parameterless constructor default
/// to <see cref="MapCatalog.Small"/> instead - the claims about the retired layout's own data moved
/// to <see cref="PhaseSixEightSlotFixtureTests"/>.
/// </summary>
public class MapLayoutInjectionTests
{
    [Fact]
    public void ParameterlessConstructor_ProducesABoard_IdenticalInEveryField_ToPassingMapCatalogSmallSlotsExplicitly()
    {
        var defaultMatch = new Match();
        var explicitMatch = new Match(MapCatalog.Small.Slots);

        Assert.Equal(defaultMatch.Bases.Count, explicitMatch.Bases.Count);
        for (var i = 0; i < defaultMatch.Bases.Count; i++)
        {
            var a = defaultMatch.Bases[i];
            var b = explicitMatch.Bases[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Position, b.Position);
            Assert.Equal(a.GarrisonCount, b.GarrisonCount);
            Assert.Equal(a.Owner?.ControllerKind, b.Owner?.ControllerKind);
            Assert.Equal(a.Type, b.Type);
            Assert.Equal(a.Level, b.Level);
        }
    }

    [Fact]
    public void LayoutTakingConstructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Match((IReadOnlyList<MapSlot>)null!));
    }

    [Fact]
    public void LayoutTakingConstructor_RejectsEmptyLayout()
    {
        Assert.Throws<ArgumentException>(() => new Match(Array.Empty<MapSlot>()));
    }

    /// <summary>
    /// A test can construct a match whose layout contains a neutral forge, proving FR-2's rules are
    /// testable before the shipped map changes (D-44) - the base starts neutral, of type Forge, at
    /// level 1, with the garrison its slot names.
    /// </summary>
    [Fact]
    public void LayoutCanPlaceANeutralForge_StartingNeutral_TypeForge_LevelOne_WithItsSlotsGarrison()
    {
        var layout = new[]
        {
            new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        };

        var match = new Match(layout);

        var forge = match.Bases.Single(b => b.Type == BaseType.Forge);
        Assert.Null(forge.Owner);
        Assert.Equal(LevelTable.MinLevel, forge.Level);
        Assert.Equal(10, forge.GarrisonCount);
    }
}
