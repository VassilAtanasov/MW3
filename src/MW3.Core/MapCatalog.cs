namespace MW3.Core;

/// <summary>
/// The fixed set of three maps a match can be built from (FR-1, D-49) - a lookup, not a registry
/// that can be added to at runtime. Slots 0-5 are identical across all three, so any test or script
/// keyed on those six bases stays valid on every map.
/// </summary>
public static class MapCatalog
{
    private static readonly MapSlot[] _sharedSlots =
    {
        new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
    };

    /// <summary>Bit-identical to the layout phases 2-5 shipped, before phase 6 FR-2 appended its two centre slots.</summary>
    public static MapDefinition Small { get; } = new(_sharedSlots, Array.Empty<MapObstacle>());

    /// <summary>
    /// The shared six slots plus two gate neutrals and a central obstacle spanning x 0.42..0.58,
    /// y 0.30..0.70. Nothing consults the obstacle yet (FR-1); it blocks movement from FR-3.
    /// </summary>
    public static MapDefinition Medium { get; } = new(
        Combine(
            new MapSlot(new MapPoint(0.50, 0.15), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.85), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel)),
        new[] { new MapObstacle(minX: 0.42, minY: 0.30, maxX: 0.58, maxY: 0.70) });

    /// <summary>
    /// The shared six slots plus a contested neutral forge flanked by two neutral towers on the
    /// centre line, each covering the forge at level 1 (0.18 away, inside the 0.20 range).
    /// </summary>
    public static MapDefinition Big { get; } = new(
        Combine(
            new MapSlot(new MapPoint(0.50, 0.32), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Tower, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.68), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Tower, LevelTable.MinLevel),
            new MapSlot(new MapPoint(0.50, 0.50), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel)),
        Array.Empty<MapObstacle>());

    /// <summary>All three maps, in <see cref="MapId"/> declaration order.</summary>
    public static IReadOnlyList<MapId> AllIds { get; } = new[] { MapId.Small, MapId.Medium, MapId.Big };

    public static MapDefinition Get(MapId id) => id switch
    {
        MapId.Small => Small,
        MapId.Medium => Medium,
        MapId.Big => Big,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, FormattableString.Invariant($"MapCatalog has no definition for {id}.")),
    };

    private static MapSlot[] Combine(params MapSlot[] extra)
    {
        var combined = new MapSlot[_sharedSlots.Length + extra.Length];
        Array.Copy(_sharedSlots, combined, _sharedSlots.Length);
        Array.Copy(extra, 0, combined, _sharedSlots.Length, extra.Length);
        return combined;
    }
}
