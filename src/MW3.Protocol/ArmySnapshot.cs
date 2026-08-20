namespace MW3.Protocol;

/// <summary>
/// One army in flight as the wire sees it - and deliberately <em>not</em> its position. Every field
/// here except <see cref="UnitCount"/> is fixed at launch (D-39, D-51), and
/// <see cref="ArmyPathMath.PositionAt(ArmyPath, long, long, long)"/> turns them plus the current tick into a position, so launch
/// data alone renders an army forever: at any frame rate, and however rarely the server sends. An
/// army's strength is the one thing that changes mid-flight, and only tower fire changes it.
///
/// The path travels as waypoints plus length rather than as an <see cref="ArmyPath"/> so that every
/// wave of a send can carry it without the receiver having to reason about which of them share an
/// instance. Waves of one send are separate armies here, exactly as they are in the rules, joined by
/// <see cref="SendId"/>.
/// </summary>
/// <param name="Id">This army's id, unique within the match.</param>
/// <param name="OwnerPlayerId">Whose army it is. Never null - an army always has an owner (D-11).</param>
/// <param name="SourceBaseId">The base it left.</param>
/// <param name="TargetBaseId">The base it is flying at. Capturing either mid-flight does not re-route it.</param>
/// <param name="UnitCount">Its current strength - what it launched with, minus what towers have shot down.</param>
/// <param name="LaunchTick">The tick it left its source base.</param>
/// <param name="ArrivalTick">The tick it resolves against its target.</param>
/// <param name="SendId">The send it belongs to; every wave of one send shares this.</param>
/// <param name="WaveIndex">This wave's 1-based index within its send. A single-arrival send is wave 1.</param>
/// <param name="WaveCount">How many waves the send has in total.</param>
/// <param name="PathWaypoints">The polyline it flies, in order, including both endpoints (D-51).</param>
/// <param name="PathLength">That polyline's arc length in normalized map units.</param>
public sealed record ArmySnapshot(
    int Id,
    int OwnerPlayerId,
    int SourceBaseId,
    int TargetBaseId,
    int UnitCount,
    long LaunchTick,
    long ArrivalTick,
    int SendId,
    int WaveIndex,
    int WaveCount,
    IReadOnlyList<MapPoint> PathWaypoints,
    double PathLength)
{
    /// <summary>
    /// The polyline this army flies, in order, including both endpoints (D-51). A path needs at
    /// least two points to be one, and that is checked here rather than in <see cref="ToPath"/>:
    /// otherwise a malformed payload deserializes cleanly and throws later, at render time, one
    /// frame after a client decided to draw the army.
    /// </summary>
    public IReadOnlyList<MapPoint> PathWaypoints { get; } = PathWaypoints is null
        ? throw new ArgumentNullException(nameof(PathWaypoints))
        : PathWaypoints.Count >= 2
            ? PathWaypoints
            : throw new ArgumentException("An army's path must have at least two waypoints.", nameof(PathWaypoints));

    /// <inheritdoc />
    public bool Equals(ArmySnapshot? other) =>
        other is not null
        && Id == other.Id
        && OwnerPlayerId == other.OwnerPlayerId
        && SourceBaseId == other.SourceBaseId
        && TargetBaseId == other.TargetBaseId
        && UnitCount == other.UnitCount
        && LaunchTick == other.LaunchTick
        && ArrivalTick == other.ArrivalTick
        && SendId == other.SendId
        && WaveIndex == other.WaveIndex
        && WaveCount == other.WaveCount
        && PathLength.Equals(other.PathLength)
        && SnapshotEquality.ListEquals(PathWaypoints, other.PathWaypoints);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked((((Id * 31) + UnitCount) * 31) + SnapshotEquality.ListHash(PathWaypoints));

    /// <summary>
    /// This army's path back in the shape <see cref="ArmyPathMath"/> takes, so a receiver holding
    /// only a snapshot resolves its position through exactly the code the rules do (D-68).
    /// </summary>
    public ArmyPath ToPath() => new(PathWaypoints, PathLength);
}
