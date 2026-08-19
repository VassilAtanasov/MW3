namespace MW3.Core;

/// <summary>
/// A map's data: an ordered list of slots and an ordered list of obstacles (D-49, D-50), extending
/// D-44's injectable-layout seam so <see cref="Match"/> can be built from a named map rather than
/// only a bare slot list. A definition with no obstacles is legal - Small and Big use one.
/// </summary>
public sealed class MapDefinition
{
    /// <param name="slots">The map's slots, in the order bases are numbered.</param>
    /// <param name="obstacles">Its obstacles, possibly none.</param>
    /// <param name="id">
    /// Which <see cref="MapCatalog"/> entry this is, or null for a definition a caller built itself -
    /// which only a test does. Carried so a match can say which map it is being played on
    /// (<see cref="Match.MapId"/>) without <see cref="MapCatalog"/> having to be searched for a
    /// definition that matches by value.
    /// </param>
    public MapDefinition(IReadOnlyList<MapSlot> slots, IReadOnlyList<MapObstacle> obstacles, MapId? id = null)
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
        Id = id;
    }

    /// <summary>Which <see cref="MapCatalog"/> entry this is, or null for a caller-built definition.</summary>
    public MapId? Id { get; }

    public IReadOnlyList<MapSlot> Slots { get; }

    public IReadOnlyList<MapObstacle> Obstacles { get; }
}
