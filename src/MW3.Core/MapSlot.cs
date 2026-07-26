namespace MW3.Core;

internal readonly record struct MapSlot(MapPoint Position, MapSlotKind Kind, int StartingGarrison);
