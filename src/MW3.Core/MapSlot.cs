namespace MW3.Core;

/// <summary>
/// One slot in a <see cref="MapLayout"/> (D-44): a fixed position, whose starting corner it belongs
/// to (or neutral), a starting garrison, and the <see cref="BaseType"/> and level the base at that
/// position starts as. Public so <see cref="Match"/>'s layout-taking constructor can accept a
/// caller-built layout - the seam that makes a neutral forge testable before the shipped map changes.
/// </summary>
public readonly record struct MapSlot(MapPoint Position, MapSlotKind Kind, int StartingGarrison, BaseType Type, int Level);
