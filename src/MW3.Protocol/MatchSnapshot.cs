namespace MW3.Protocol;

/// <summary>
/// A whole match, expressed as data: everything a renderer needs to draw one and everything a server
/// needs to describe one, and nothing a client could use to decide an outcome (D-57). Immutable
/// after construction, and JSON-shaped - no polymorphism, no cycles, no engine types.
///
/// Players are a list rather than <c>Human</c>/<c>Ai</c> fields, with the local player named by id.
/// The rules stay two-player this phase (that is the Multiplayer project's job), but the snapshot
/// costs nothing to make player-agnostic and a later phase should not have to redefine the wire to
/// add a third player.
///
/// Two things are deliberately absent. There is no army position or progress - both are pure
/// functions of data already here (see <see cref="ArmyPathMath"/>), and sending them would make
/// smooth motion depend on the send rate. And there is no menu or selected send strength: those are
/// presentation state (D-26), owned by whichever screen is rendering, not by the match.
/// </summary>
/// <param name="ProtocolVersion">
/// The shape of this payload, <see cref="CurrentProtocolVersion"/> at the time it was built. Carried
/// so a client and a server that disagree fail loudly on the first message rather than subtly on the
/// first field that moved.
/// </param>
/// <param name="MapId">
/// The name of the map this match was built from - "Small", "Medium", "Big" - or null when it was
/// built from a bare layout, which only a test does. A name rather than the <c>MapId</c> enum,
/// because that enum belongs to the rules' map catalogue and stays there (D-49 leaves the map file
/// format to the Campaigns project).
/// </param>
/// <param name="ElapsedTicks">Ticks the match has advanced through - the sequence number of this snapshot.</param>
/// <param name="Outcome">Undecided, or decided in one player's favour.</param>
/// <param name="LocalPlayerId">
/// Which of <paramref name="Players"/> this snapshot was built for. It is the only player whose
/// available actions are populated.
/// </param>
/// <param name="Obstacles">The map's obstacles, which block movement (phase 7 D-54) and are drawn.</param>
/// <param name="Players">Every player, in the order the match holds them.</param>
/// <param name="Bases">Every base, in id order.</param>
/// <param name="Armies">Every army in flight, in the order the match holds them.</param>
public sealed record MatchSnapshot(
    int ProtocolVersion,
    string? MapId,
    long ElapsedTicks,
    MatchOutcome Outcome,
    int LocalPlayerId,
    IReadOnlyList<MapObstacle> Obstacles,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<BaseSnapshot> Bases,
    IReadOnlyList<ArmySnapshot> Armies)
{
    /// <summary>Every player, in the order the match holds them.</summary>
    public IReadOnlyList<PlayerSnapshot> Players { get; } = Players ?? throw new ArgumentNullException(nameof(Players));

    /// <summary>Every base, in id order.</summary>
    public IReadOnlyList<BaseSnapshot> Bases { get; } = Bases ?? throw new ArgumentNullException(nameof(Bases));

    /// <summary>Every army in flight, in the order the match holds them.</summary>
    public IReadOnlyList<ArmySnapshot> Armies { get; } = Armies ?? throw new ArgumentNullException(nameof(Armies));

    /// <summary>The map's obstacles, which block movement (phase 7 D-54) and are drawn.</summary>
    public IReadOnlyList<MapObstacle> Obstacles { get; } = Obstacles ?? throw new ArgumentNullException(nameof(Obstacles));

    /// <summary>
    /// The version every snapshot this build produces carries. Bumped when a field is added,
    /// removed or reinterpreted - never for a value change.
    /// </summary>
    public const int CurrentProtocolVersion = 3;

    /// <inheritdoc />
    public bool Equals(MatchSnapshot? other) =>
        other is not null
        && ProtocolVersion == other.ProtocolVersion
        && MapId == other.MapId
        && ElapsedTicks == other.ElapsedTicks
        && Outcome == other.Outcome
        && LocalPlayerId == other.LocalPlayerId
        && SnapshotEquality.ListEquals(Obstacles, other.Obstacles)
        && SnapshotEquality.ListEquals(Players, other.Players)
        && SnapshotEquality.ListEquals(Bases, other.Bases)
        && SnapshotEquality.ListEquals(Armies, other.Armies);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked((((((ProtocolVersion * 31) + ElapsedTicks.GetHashCode()) * 31)
            + SnapshotEquality.ListHash(Bases)) * 31) + SnapshotEquality.ListHash(Armies));

    /// <summary>
    /// The player <see cref="LocalPlayerId"/> names, or null if this snapshot does not carry them.
    ///
    /// A method rather than a property on purpose: <c>System.Text.Json</c> serializes every public
    /// gettable property, so as a property this would put a second copy of one player's record on
    /// the wire - data that is already in <see cref="Players"/>, that no reader needs sent, and that
    /// a deserializer would silently drop on the way back because it has no setter. Two encodings of
    /// one fact is how a payload starts being able to contradict itself.
    /// </summary>
    public PlayerSnapshot? FindLocalPlayer()
    {
        for (var i = 0; i < Players.Count; i++)
        {
            if (Players[i].Id == LocalPlayerId)
            {
                return Players[i];
            }
        }

        return null;
    }
}
