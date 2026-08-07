namespace MW3.Core;

/// <summary>
/// A map's data: an ordered list of slots and an ordered list of obstacles (D-49, D-50), extending
/// D-44's injectable-layout seam so <see cref="Match"/> can be built from a named map rather than
/// only a bare slot list. A definition with no obstacles is legal - Small and Big use one.
/// </summary>
public sealed class MapDefinition
{
    public MapDefinition(IReadOnlyList<MapSlot> slots, IReadOnlyList<MapObstacle> obstacles)
    {
        if (slots is null)
        {
            throw new ArgumentNullException(nameof(slots));
        }

        if (obstacles is null)
        {
            throw new ArgumentNullException(nameof(obstacles));
        }

        if (slots.Count == 0)
        {
            throw new ArgumentException("A map definition's slot list must contain at least one slot.", nameof(slots));
        }

        for (var i = 0; i < slots.Count; i++)
        {
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Contains(slots[i].Position))
                {
                    throw new ArgumentException(
                        FormattableString.Invariant($"Slot {i} sits inside one of this map's obstacles."),
                        nameof(slots));
                }
            }
        }

        Slots = slots;
        Obstacles = obstacles;
    }

    public IReadOnlyList<MapSlot> Slots { get; }

    public IReadOnlyList<MapObstacle> Obstacles { get; }
}
