namespace MW3.Protocol;

/// <summary>
/// One base as the wire sees it. Neutral is the absence of an owner - <c>OwnerPlayerId</c> is null,
/// never a sentinel id (D-11).
///
/// The table-derived values (garrison cap, upgrade cost, defence percentage, ring thickness, and the
/// two level ceilings) travel as values rather than being left for the receiver to look up, because
/// after FR-3 the receiver has no <c>LevelTable</c> to look them up in. That is the point of D-57,
/// not a cost of it: a client that can compute a garrison cap is a client holding a copy of the
/// rules, and a copy can drift from the server's.
/// </summary>
/// <param name="Id">This base's id, stable for the life of the match.</param>
/// <param name="Position">Its fixed position in normalized 0..1 map units.</param>
/// <param name="OwnerPlayerId">Its owner's id, or null when neutral (D-11).</param>
/// <param name="Type">Producer, tower or forge.</param>
/// <param name="Level">Its current level.</param>
/// <param name="GarrisonCount">Units currently garrisoned - can exceed the cap (D-21).</param>
/// <param name="GarrisonCap">
/// The garrison this base produces up to at its current level, or null if its type has no cap (a
/// tower). A production ceiling, not a storage limit (D-21).
/// </param>
/// <param name="UpgradeCost">
/// Units it costs to raise this base one level, or null when there is no next level to price - a
/// base already at its type's maximum upgradable level, or a forge, which has no upgrade path at
/// all. Null rather than 0: "free" and "impossible" are different answers.
/// </param>
/// <param name="DefencePercentage">What it defends at, as a percentage of the 1:1 baseline (D-29).</param>
/// <param name="RingThicknessFractionOfRadius">How thick its level ring is drawn.</param>
/// <param name="MaxLevel">The highest level its type's ladder defines.</param>
/// <param name="MaxUpgradableLevel">The highest level it can reach by upgrading, which can be lower.</param>
/// <param name="ProductionProgressTicks">Ticks accumulated toward its next unit - simulation state determinism covers (D-12).</param>
/// <param name="Construction">What it is building, or null.</param>
/// <param name="LastOwnerChangeTick">
/// The tick it last changed owner, or null if it never has. The client draws nothing from it, but
/// the recapture grace (D-30) is decided against it and FR-2 diffs it.
/// </param>
/// <param name="OwnerBeforeLastChangePlayerId">
/// The owner it had immediately before that change, or null if that owner was neutral or it has
/// never changed hands.
/// </param>
/// <param name="LastFireTick">
/// The tick it last fired as a tower, or null if it never has. This is the one field the renderer
/// derives an animation from by comparing it against the current tick, which is why no separate
/// "tower fired" event is needed to keep muzzle flashes working.
/// </param>
/// <param name="AvailableActions">
/// What the local player can do to this base right now, in <c>Match.AvailableActions</c> order
/// (D-48, D-66) - empty for every base the local player does not own, because there is nothing for
/// them to do to it and nothing for the client to learn from being told why.
/// </param>
/// <param name="RangeUnits">
/// How far this base shoots, as a Euclidean distance in the same normalized 0..1 map units
/// <see cref="Position"/> uses - null for a base whose type has no range at all, which is every type
/// but a tower. Added at FR-3 because the renderer drew its range ring straight out of
/// <c>LevelTable</c>, the last table read left on the client; FR-1's byte-identical dump could not
/// catch it, because <c>--dump-state</c> never prints a range.
/// </param>
public sealed record BaseSnapshot(
    int Id,
    MapPoint Position,
    int? OwnerPlayerId,
    BaseType Type,
    int Level,
    int GarrisonCount,
    int? GarrisonCap,
    int? UpgradeCost,
    int DefencePercentage,
    double RingThicknessFractionOfRadius,
    int MaxLevel,
    int MaxUpgradableLevel,
    long ProductionProgressTicks,
    PendingConstructionSnapshot? Construction,
    long? LastOwnerChangeTick,
    int? OwnerBeforeLastChangePlayerId,
    long? LastFireTick,
    IReadOnlyList<BaseActionSnapshot> AvailableActions,
    double? RangeUnits)
{
    /// <summary>
    /// What the local player can do to this base right now, empty for a base they do not own.
    /// Validated here rather than left to the first reader: a deserializer handed
    /// <c>"AvailableActions": null</c> would otherwise produce a snapshot that only fails when a
    /// menu is opened, which is a long way from where the bad payload arrived.
    /// </summary>
    public IReadOnlyList<BaseActionSnapshot> AvailableActions { get; } =
        AvailableActions ?? throw new ArgumentNullException(nameof(AvailableActions));

    /// <inheritdoc />
    public bool Equals(BaseSnapshot? other) =>
        other is not null
        && Id == other.Id
        && Position.Equals(other.Position)
        && OwnerPlayerId == other.OwnerPlayerId
        && Type == other.Type
        && Level == other.Level
        && GarrisonCount == other.GarrisonCount
        && GarrisonCap == other.GarrisonCap
        && UpgradeCost == other.UpgradeCost
        && DefencePercentage == other.DefencePercentage
        && RingThicknessFractionOfRadius.Equals(other.RingThicknessFractionOfRadius)
        && MaxLevel == other.MaxLevel
        && MaxUpgradableLevel == other.MaxUpgradableLevel
        && ProductionProgressTicks == other.ProductionProgressTicks
        && Construction == other.Construction
        && LastOwnerChangeTick == other.LastOwnerChangeTick
        && OwnerBeforeLastChangePlayerId == other.OwnerBeforeLastChangePlayerId
        && LastFireTick == other.LastFireTick
        && Nullable.Equals(RangeUnits, other.RangeUnits)
        && SnapshotEquality.ListEquals(AvailableActions, other.AvailableActions);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked((((Id * 31) + Level) * 31) + SnapshotEquality.ListHash(AvailableActions));
}
