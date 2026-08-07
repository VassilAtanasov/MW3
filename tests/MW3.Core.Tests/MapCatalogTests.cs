namespace MW3.Core.Tests;

/// <summary>
/// FR-1: the fixed three-map catalog, and the geometry each map must satisfy - asserted for all
/// three so a later coordinate change cannot quietly break one of them.
/// </summary>
public class MapCatalogTests
{
    private static readonly MapSlot[] _sharedSlots =
    {
        new(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
    };

    private static IEnumerable<MapDefinition> AllMaps => new[] { MapCatalog.Small, MapCatalog.Medium, MapCatalog.Big };

    // --- MapId / MapCatalog surface ---

    [Fact]
    public void MapId_HasExactlyThreeMembers()
    {
        Assert.Equal(new[] { MapId.Small, MapId.Medium, MapId.Big }, Enum.GetValues<MapId>());
    }

    [Fact]
    public void AllIds_EnumeratesAllThree_InSmallMediumBigOrder()
    {
        Assert.Equal(new[] { MapId.Small, MapId.Medium, MapId.Big }, MapCatalog.AllIds);
    }

    [Fact]
    public void Get_ReturnsTheMatchingDefinition_ForEachId()
    {
        Assert.Same(MapCatalog.Small, MapCatalog.Get(MapId.Small));
        Assert.Same(MapCatalog.Medium, MapCatalog.Get(MapId.Medium));
        Assert.Same(MapCatalog.Big, MapCatalog.Get(MapId.Big));
    }

    [Fact]
    public void Get_ThrowsForAnUndefinedMapId_RatherThanReturningADefault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapCatalog.Get((MapId)999));
    }

    // --- Slots 0-5 identical across all three maps ---

    [Fact]
    public void EveryMap_SlotsZeroToFive_MatchTheSharedSixExactly()
    {
        foreach (var map in AllMaps)
        {
            for (var i = 0; i < _sharedSlots.Length; i++)
            {
                Assert.Equal(_sharedSlots[i], map.Slots[i]);
            }
        }
    }

    // --- The three maps' exact composition ---

    [Fact]
    public void Small_IsExactlyTheSharedSixSlots_NoObstacle()
    {
        Assert.Equal(6, MapCatalog.Small.Slots.Count);
        Assert.Empty(MapCatalog.Small.Obstacles);
        for (var i = 0; i < _sharedSlots.Length; i++)
        {
            Assert.Equal(_sharedSlots[i], MapCatalog.Small.Slots[i]);
        }
    }

    [Fact]
    public void Medium_HasEightSlots_TwoGateNeutralsAndOneObstacle()
    {
        Assert.Equal(8, MapCatalog.Medium.Slots.Count);

        var topGate = MapCatalog.Medium.Slots[6];
        Assert.Equal(new MapPoint(0.50, 0.15), topGate.Position);
        Assert.Equal(MapSlotKind.Neutral, topGate.Kind);
        Assert.Equal(5, topGate.StartingGarrison);
        Assert.Equal(BaseType.Producer, topGate.Type);

        var bottomGate = MapCatalog.Medium.Slots[7];
        Assert.Equal(new MapPoint(0.50, 0.85), bottomGate.Position);
        Assert.Equal(MapSlotKind.Neutral, bottomGate.Kind);
        Assert.Equal(5, bottomGate.StartingGarrison);
        Assert.Equal(BaseType.Producer, bottomGate.Type);

        Assert.Single(MapCatalog.Medium.Obstacles);
        var obstacle = MapCatalog.Medium.Obstacles[0];
        Assert.Equal(0.42, obstacle.MinX);
        Assert.Equal(0.58, obstacle.MaxX);
        Assert.Equal(0.30, obstacle.MinY);
        Assert.Equal(0.70, obstacle.MaxY);
    }

    [Fact]
    public void Big_HasNineSlots_TwoTowersAndAForge_NoObstacle()
    {
        Assert.Equal(9, MapCatalog.Big.Slots.Count);
        Assert.Empty(MapCatalog.Big.Obstacles);

        var topTower = MapCatalog.Big.Slots[6];
        Assert.Equal(new MapPoint(0.50, 0.32), topTower.Position);
        Assert.Equal(BaseType.Tower, topTower.Type);
        Assert.Equal(10, topTower.StartingGarrison);
        Assert.Equal(MapSlotKind.Neutral, topTower.Kind);
        Assert.Equal(LevelTable.MinLevel, topTower.Level);

        var bottomTower = MapCatalog.Big.Slots[7];
        Assert.Equal(new MapPoint(0.50, 0.68), bottomTower.Position);
        Assert.Equal(BaseType.Tower, bottomTower.Type);
        Assert.Equal(10, bottomTower.StartingGarrison);

        var forge = MapCatalog.Big.Slots[8];
        Assert.Equal(new MapPoint(0.50, 0.50), forge.Position);
        Assert.Equal(BaseType.Forge, forge.Type);
        Assert.Equal(10, forge.StartingGarrison);
    }

    [Fact]
    public void EveryMap_EverySlot_IsLevelOne()
    {
        foreach (var map in AllMaps)
        {
            foreach (var slot in map.Slots)
            {
                Assert.Equal(LevelTable.MinLevel, slot.Level);
            }
        }
    }

    // --- Geometry: shared across all three maps ---

    [Fact]
    public void EveryMap_NoTowerRangeAtAnyLevel_ReachesEitherStartBase()
    {
        foreach (var map in AllMaps)
        {
            var humanStart = map.Slots.Single(s => s.Kind == MapSlotKind.HumanStart).Position;
            var aiStart = map.Slots.Single(s => s.Kind == MapSlotKind.AiStart).Position;

            var nearestToAnyStart = double.MaxValue;
            foreach (var slot in map.Slots)
            {
                if (slot.Kind is MapSlotKind.HumanStart or MapSlotKind.AiStart)
                {
                    continue;
                }

                nearestToAnyStart = Math.Min(nearestToAnyStart, Math.Min(Distance(humanStart, slot.Position), Distance(aiStart, slot.Position)));
            }

            Assert.Equal(0.3397, nearestToAnyStart, precision: 4);

            for (var level = LevelTable.MinLevel; level <= LevelTable.Tower.MaxLevel; level++)
            {
                Assert.True(LevelTable.Tower.RangeUnits(level) < nearestToAnyStart);
            }
        }
    }

    [Fact]
    public void EveryMap_NoSlot_SitsCloserThanTheMoraleMeterClearance_ToAnyMapEdge()
    {
        const double minClearance = 0.12;

        foreach (var map in AllMaps)
        {
            foreach (var slot in map.Slots)
            {
                var clearance = Math.Min(Math.Min(slot.Position.X, 1.0 - slot.Position.X), Math.Min(slot.Position.Y, 1.0 - slot.Position.Y));
                Assert.True(clearance >= minClearance, FormattableString.Invariant($"Slot at {slot.Position} has clearance {clearance}, below {minClearance}."));
            }
        }
    }

    // --- Geometry: Big only ---

    [Fact]
    public void Big_EachNeutralTower_CoversTheNeutralForge_AtLevelOne()
    {
        var forge = MapCatalog.Big.Slots.Single(s => s.Type == BaseType.Forge);
        var towers = MapCatalog.Big.Slots.Where(s => s.Type == BaseType.Tower).ToList();
        var range = LevelTable.Tower.RangeUnits(LevelTable.MinLevel);

        Assert.Equal(2, towers.Count);
        foreach (var tower in towers)
        {
            var distance = Distance(tower.Position, forge.Position);
            Assert.Equal(0.18, distance, precision: 3);
            Assert.True(distance <= range);
        }
    }

    [Fact]
    public void Big_EachNeutralTower_CoversExactlyTheTwoFlankNeutralsOnItsOwnHalf()
    {
        var range = LevelTable.Tower.RangeUnits(LevelTable.MinLevel);
        var topTower = MapCatalog.Big.Slots.Single(s => s.Type == BaseType.Tower && s.Position.Y < 0.50);
        var bottomTower = MapCatalog.Big.Slots.Single(s => s.Type == BaseType.Tower && s.Position.Y > 0.50);
        var topFlanks = MapCatalog.Big.Slots.Where(s => s.Kind == MapSlotKind.Neutral && s.Type == BaseType.Producer && s.Position.Y < 0.50).ToList();
        var bottomFlanks = MapCatalog.Big.Slots.Where(s => s.Kind == MapSlotKind.Neutral && s.Type == BaseType.Producer && s.Position.Y > 0.50).ToList();

        Assert.Equal(2, topFlanks.Count);
        Assert.Equal(2, bottomFlanks.Count);

        foreach (var flank in topFlanks)
        {
            Assert.True(Distance(topTower.Position, flank.Position) <= range);
            Assert.False(Distance(bottomTower.Position, flank.Position) <= range);
        }

        foreach (var flank in bottomFlanks)
        {
            Assert.True(Distance(bottomTower.Position, flank.Position) <= range);
            Assert.False(Distance(topTower.Position, flank.Position) <= range);
        }
    }

    [Fact]
    public void Big_TheTwoNeutralTowers_DoNotCoverEachOther_AtAnyLevel()
    {
        var towers = MapCatalog.Big.Slots.Where(s => s.Type == BaseType.Tower).ToList();
        var distance = Distance(towers[0].Position, towers[1].Position);

        Assert.Equal(0.36, distance, precision: 3);
        for (var level = LevelTable.MinLevel; level <= LevelTable.Tower.MaxLevel; level++)
        {
            Assert.True(LevelTable.Tower.RangeUnits(level) < distance);
        }
    }

    // --- Geometry: Medium only ---

    [Fact]
    public void Medium_Obstacle_BlocksTheStraightLineBetweenTheTwoStartBases()
    {
        var humanStart = MapCatalog.Medium.Slots.Single(s => s.Kind == MapSlotKind.HumanStart).Position;
        var aiStart = MapCatalog.Medium.Slots.Single(s => s.Kind == MapSlotKind.AiStart).Position;

        Assert.True(SegmentIntersectsObstacle(humanStart, aiStart, MapCatalog.Medium.Obstacles[0]));
    }

    [Fact]
    public void Medium_Obstacle_BlocksTheStraightLineBetweenTheTwoGateSlots()
    {
        var gates = MapCatalog.Medium.Slots.Where(s => s.Position.X == 0.50 && s.Type == BaseType.Producer && s.Kind == MapSlotKind.Neutral).ToList();
        Assert.Equal(2, gates.Count);

        Assert.True(SegmentIntersectsObstacle(gates[0].Position, gates[1].Position, MapCatalog.Medium.Obstacles[0]));
    }

    [Fact]
    public void Medium_BothGateSlots_AreReachableFromBothStartBases_ByAnUnobstructedLine()
    {
        var humanStart = MapCatalog.Medium.Slots.Single(s => s.Kind == MapSlotKind.HumanStart).Position;
        var aiStart = MapCatalog.Medium.Slots.Single(s => s.Kind == MapSlotKind.AiStart).Position;
        var gates = MapCatalog.Medium.Slots.Where(s => s.Position.X == 0.50 && s.Type == BaseType.Producer && s.Kind == MapSlotKind.Neutral).ToList();

        foreach (var start in new[] { humanStart, aiStart })
        {
            foreach (var gate in gates)
            {
                Assert.False(SegmentIntersectsObstacle(start, gate.Position, MapCatalog.Medium.Obstacles[0]));
            }
        }
    }

    // --- Match takes a definition ---

    [Fact]
    public void Match_FromADefinition_BuildsBasesInSlotOrder_PreservingIdsAsSlotIndices()
    {
        var match = new Match(MapCatalog.Big);

        Assert.Equal(MapCatalog.Big.Slots.Count, match.Bases.Count);
        for (var i = 0; i < MapCatalog.Big.Slots.Count; i++)
        {
            var slot = MapCatalog.Big.Slots[i];
            var b = match.Bases[i];
            Assert.Equal(i, b.Id);
            Assert.Equal(slot.Position, b.Position);
            Assert.Equal(slot.StartingGarrison, b.GarrisonCount);
            Assert.Equal(slot.Type, b.Type);
            Assert.Equal(slot.Level, b.Level);
        }
    }

    [Fact]
    public void Match_FromADefinition_ExposesItsObstacles()
    {
        var match = new Match(MapCatalog.Medium);
        Assert.Equal(MapCatalog.Medium.Obstacles, match.Obstacles);
    }

    [Fact]
    public void Match_FromASlotList_ExposesNoObstacles()
    {
        var match = new Match(_sharedSlots);
        Assert.Empty(match.Obstacles);
    }

    [Fact]
    public void Match_DefinitionConstructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Match((MapDefinition)null!));
    }

    [Fact]
    public void Match_FromMapCatalogSmall_IsFieldForFieldIdentical_ToMatchFromTheSharedSixSlots()
    {
        var fromCatalog = new Match(MapCatalog.Small);
        var fromSlots = new Match(_sharedSlots);

        Assert.Equal(fromSlots.Bases.Count, fromCatalog.Bases.Count);
        for (var i = 0; i < fromSlots.Bases.Count; i++)
        {
            var a = fromCatalog.Bases[i];
            var b = fromSlots.Bases[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Position, b.Position);
            Assert.Equal(a.GarrisonCount, b.GarrisonCount);
            Assert.Equal(a.Type, b.Type);
            Assert.Equal(a.Level, b.Level);
        }
    }

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Liang-Barsky segment-rectangle intersection, against the obstacle's own bounds.</summary>
    private static bool SegmentIntersectsObstacle(MapPoint from, MapPoint to, MapObstacle obstacle)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var tMin = 0.0;
        var tMax = 1.0;

        var p = new[] { -dx, dx, -dy, dy };
        var q = new[] { from.X - obstacle.MinX, obstacle.MaxX - from.X, from.Y - obstacle.MinY, obstacle.MaxY - from.Y };

        for (var i = 0; i < 4; i++)
        {
            if (p[i] == 0.0)
            {
                if (q[i] < 0.0)
                {
                    return false;
                }

                continue;
            }

            var t = q[i] / p[i];
            if (p[i] < 0.0)
            {
                tMin = Math.Max(tMin, t);
            }
            else
            {
                tMax = Math.Min(tMax, t);
            }
        }

        return tMin <= tMax;
    }
}
