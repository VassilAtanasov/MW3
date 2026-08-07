namespace MW3.Core.Tests;

/// <summary>
/// Claims about the retired phase-6 shipped board's own data, moved here from
/// <c>MapLayoutInjectionTests</c> at FR-2 when <c>MapLayout</c> was deleted and its data preserved
/// only as <see cref="PhaseSixEightSlotFixture"/> (D-49). These pin the fixture's own values, not
/// anything the running application still produces.
/// </summary>
public class PhaseSixEightSlotFixtureTests
{
    [Fact]
    public void Slots_FirstSixAreUnchanged_LevelOneProducerSlots()
    {
        Assert.Equal(8, PhaseSixEightSlotFixture.Slots.Count);

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
            Assert.Equal(expectedFirstSix[i], PhaseSixEightSlotFixture.Slots[i]);
        }
    }

    /// <summary>Slot 6: the neutral forge, on the centre line (phase 6 FR-2).</summary>
    [Fact]
    public void Slot6_IsTheNeutralForge()
    {
        var slot = PhaseSixEightSlotFixture.Slots[6];
        Assert.Equal(MapSlotKind.Neutral, slot.Kind);
        Assert.Equal(new MapPoint(0.50, 0.20), slot.Position);
        Assert.Equal(BaseType.Forge, slot.Type);
        Assert.Equal(LevelTable.MinLevel, slot.Level);
        Assert.Equal(10, slot.StartingGarrison);
    }

    /// <summary>Slot 7: the neutral tower, on the centre line (phase 6 FR-2).</summary>
    [Fact]
    public void Slot7_IsTheNeutralTower()
    {
        var slot = PhaseSixEightSlotFixture.Slots[7];
        Assert.Equal(MapSlotKind.Neutral, slot.Kind);
        Assert.Equal(new MapPoint(0.50, 0.80), slot.Position);
        Assert.Equal(BaseType.Tower, slot.Type);
        Assert.Equal(LevelTable.MinLevel, slot.Level);
        Assert.Equal(10, slot.StartingGarrison);
    }

    /// <summary>Both centre-line slots are equidistant from both starts (phase 6 FR-2).</summary>
    [Fact]
    public void NewSlots_AreEquidistant_FromBothStarts()
    {
        var humanStart = new MapPoint(0.12, 0.50);
        var aiStart = new MapPoint(0.88, 0.50);

        foreach (var slotIndex in new[] { 6, 7 })
        {
            var position = PhaseSixEightSlotFixture.Slots[slotIndex].Position;
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
}
