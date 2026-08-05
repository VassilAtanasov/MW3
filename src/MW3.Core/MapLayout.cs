namespace MW3.Core;

/// <summary>
/// The shipped six-base map (REQUIREMENTS.md §6: one map, no map format). The human and AI bases
/// face each other across four neutral bases, two per flank, so neither side starts with a
/// positional advantage. Public (D-44, amended at FR-1's kickoff) so it can be read as the default
/// value for <see cref="Match"/>'s layout-taking constructor - it is not, itself, a second map or a
/// map-selection mechanism.
/// </summary>
public static class MapLayout
{
    public static IReadOnlyList<MapSlot> Slots { get; } = new[]
    {
        new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5, BaseType.Producer, LevelTable.MinLevel),
    };
}
