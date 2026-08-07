namespace MW3.Core.Tests;

/// <summary>
/// The phase-6 shipped eight-slot board (the original <c>MapLayout.Slots</c>, deleted at FR-2),
/// preserved as a test fixture rather than a shipped map (D-49). Phase-6 tests that assert
/// behaviour against a single neutral forge and a single neutral tower - rather than against
/// <see cref="MapCatalog.Big"/>'s different geometry (two towers, a re-centred forge) - construct
/// their <see cref="Match"/> from this fixture instead of relying on the parameterless constructor,
/// which now defaults to <see cref="MapCatalog.Small"/>.
/// </summary>
internal static class PhaseSixEightSlotFixture
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
