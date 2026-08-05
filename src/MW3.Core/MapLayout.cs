namespace MW3.Core;

/// <summary>
/// The shipped eight-base map (REQUIREMENTS.md §6: one map, no map format). The human and AI bases
/// face each other across four neutral bases, two per flank, so neither side starts with a
/// positional advantage. Public (D-44, amended at FR-1's kickoff) so it can be read as the default
/// value for <see cref="Match"/>'s layout-taking constructor - it is not, itself, a second map or a
/// map-selection mechanism.
/// <para>
/// Phase 6 FR-2 <b>appends</b> a contested neutral forge and neutral tower on the centre line
/// (slots 6 and 7), rather than inserting them, so bases 0-5 keep their ids and meanings and every
/// script or test that indexed the original six bases stays valid for those six. Both new slots sit
/// at <c>x = 0.5</c>, exactly equidistant from the human start (0.12, 0.50) and the AI start
/// (0.88, 0.50) - the same positional-fairness guarantee the original four flank neutrals give each
/// other. Starting garrison 10, double an ordinary neutral's 5: these are prizes, not expansion
/// room, so a 5-unit garrison would be taken in the opening seconds by whoever sends first
/// (REQUIREMENTS.md §4 "Tuning values").
/// </para>
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
        new MapSlot(new MapPoint(0.50, 0.20), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Forge, LevelTable.MinLevel),
        new MapSlot(new MapPoint(0.50, 0.80), MapSlotKind.Neutral, StartingGarrison: 10, BaseType.Tower, LevelTable.MinLevel),
    };
}
