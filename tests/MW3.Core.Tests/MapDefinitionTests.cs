namespace MW3.Core.Tests;

/// <summary>FR-1: <see cref="MapDefinition"/> carries slots and obstacles, validated at construction.</summary>
public class MapDefinitionTests
{
    private static readonly MapSlot[] _validSlots =
    {
        new(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
    };

    [Fact]
    public void Constructor_RejectsAnEmptySlotList()
    {
        Assert.Throws<ArgumentException>(() => new MapDefinition(Array.Empty<MapSlot>(), Array.Empty<MapObstacle>()));
    }

    [Fact]
    public void Constructor_AcceptsNoObstacles()
    {
        var definition = new MapDefinition(_validSlots, Array.Empty<MapObstacle>());
        Assert.Empty(definition.Obstacles);
        Assert.Equal(_validSlots, definition.Slots);
    }

    [Fact]
    public void Constructor_RejectsASlotStandingInsideAnObstacle_NamingTheSlotIndex()
    {
        var slots = new[]
        {
            _validSlots[0],
            _validSlots[1],
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        };
        var obstacles = new[] { new MapObstacle(minX: 0.42, minY: 0.30, maxX: 0.58, maxY: 0.70) };

        var ex = Assert.Throws<ArgumentException>(() => new MapDefinition(slots, obstacles));
        Assert.Contains("2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_AcceptsASlotOutsideEveryObstacle()
    {
        var obstacles = new[] { new MapObstacle(minX: 0.42, minY: 0.30, maxX: 0.58, maxY: 0.70) };
        var definition = new MapDefinition(_validSlots, obstacles);

        Assert.Single(definition.Obstacles);
    }
}
