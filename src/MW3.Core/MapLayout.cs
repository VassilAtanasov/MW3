namespace MW3.Core;

/// <summary>
/// The one hardcoded six-base map (REQUIREMENTS.md §6: one map, no map format). The human and AI
/// bases face each other across four neutral bases, two per flank, so neither side starts with a
/// positional advantage.
/// </summary>
internal static class MapLayout
{
    internal static IReadOnlyList<MapSlot> Slots { get; } = new[]
    {
        new MapSlot(new MapPoint(0.12, 0.50), MapSlotKind.HumanStart, StartingGarrison: 10),
        new MapSlot(new MapPoint(0.88, 0.50), MapSlotKind.AiStart, StartingGarrison: 10),
        new MapSlot(new MapPoint(0.35, 0.25), MapSlotKind.Neutral, StartingGarrison: 5),
        new MapSlot(new MapPoint(0.35, 0.75), MapSlotKind.Neutral, StartingGarrison: 5),
        new MapSlot(new MapPoint(0.65, 0.25), MapSlotKind.Neutral, StartingGarrison: 5),
        new MapSlot(new MapPoint(0.65, 0.75), MapSlotKind.Neutral, StartingGarrison: 5),
    };
}
