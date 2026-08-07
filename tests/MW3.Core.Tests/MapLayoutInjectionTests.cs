namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-1, D-44: the map layout is a value <see cref="Match"/> accepts, defaulting to
/// <see cref="MapLayout.Slots"/>. This is the seam that makes a neutral forge (and later, FR-2's
/// contested one) testable before the shipped map itself changes.
/// </summary>
public class MapLayoutInjectionTests
{
    [Fact]
    public void ParameterlessConstructor_ProducesABoard_IdenticalInEveryField_ToPassingMapLayoutSlotsExplicitly()
    {
        var defaultMatch = new Match();
        var explicitMatch = new Match(MapLayout.Slots);

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

    /// <summary>
    /// Phase 6 FR-2: the layout grows from six to eight slots by appending a neutral forge and a
    /// neutral tower, so the first six stay level-1 producers, element-by-element identical to the
    /// six literals that shipped through phase 5 - bases 0-5 keep their ids and meanings.
    /// </summary>
    [Fact]
    public void MapLayout_Slots_FirstSixAreUnchanged_LevelOneProducerSlots()
    {
        Assert.Equal(8, MapLayout.Slots.Count);

        var expectedFirstSix = new[]
        {
            new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        };

        for (var i = 0; i < expectedFirstSix.Length; i++)
        {
            Assert.Equal(expectedFirstSix[i], MapLayout.Slots[i]);
        }
    }

    /// <summary>Slot 6: the neutral forge, on the centre line (FR-2).</summary>
    [Fact]
    public void MapLayout_Slot6_IsTheNeutralForge()
    {
        var slot = MapLayout.Slots[6];
        Assert.Equal(MapSlotKind.Neutral, slot.Kind);
        Assert.Equal(new MapPoint(0.50, 0.20), slot.Position);
        Assert.Equal(BaseType.Forge, slot.Type);
        Assert.Equal(LevelTable.MinLevel, slot.Level);
        Assert.Equal(10, slot.StartingGarrison);
    }

    /// <summary>Slot 7: the neutral tower, on the centre line (FR-2).</summary>
    [Fact]
    public void MapLayout_Slot7_IsTheNeutralTower()
    {
        var slot = MapLayout.Slots[7];
        Assert.Equal(MapSlotKind.Neutral, slot.Kind);
        Assert.Equal(new MapPoint(0.50, 0.80), slot.Position);
        Assert.Equal(BaseType.Tower, slot.Type);
        Assert.Equal(LevelTable.MinLevel, slot.Level);
        Assert.Equal(10, slot.StartingGarrison);
    }

    /// <summary>
    /// Both new centre-line slots are exactly equidistant from the human start (0.12, 0.50) and the
    /// AI start (0.88, 0.50), computed rather than eyeballed - the same positional-fairness guarantee
    /// the original four flank neutrals give each other (FR-2).
    /// </summary>
    [Fact]
    public void NewSlots_AreEquidistant_FromBothStarts()
    {
        var humanStart = new MapPoint(0.12, 0.50);
        var aiStart = new MapPoint(0.88, 0.50);

        foreach (var slotIndex in new[] { 6, 7 })
        {
            var position = MapLayout.Slots[slotIndex].Position;
            var toHuman = Distance(position, humanStart);
            var toAi = Distance(position, aiStart);
            Assert.Equal(toHuman, toAi, precision: 10);
        }
    }

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
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
