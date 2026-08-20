namespace MW3.Transport;

/// <summary>
/// One frame on the wire (D-64). Every message carries <see cref="ProtocolVersion"/>, so a mismatch
/// is a clean refusal naming both versions rather than a partial parse of a payload shaped
/// differently than the reader expects. One record for every message kind, with
/// <see cref="Kind"/> selecting which of the rest are populated - the same flattening
/// <c>GatewayCommand</c> already uses for the same JSON-has-no-discriminated-union reason.
/// </summary>
/// <param name="Kind">Which message this is.</param>
/// <param name="ProtocolVersion">The sender's protocol version - <see cref="MatchSnapshot.CurrentProtocolVersion"/> at the time it was built.</param>
/// <param name="MapNames"><see cref="WireMessageKind.Welcome"/>: every map, in catalogue order.</param>
/// <param name="MapName"><see cref="WireMessageKind.CreateSession"/>: the map to play.</param>
/// <param name="TimeScale"><see cref="WireMessageKind.CreateSession"/>: the session's time scale (D-62, D-79).</param>
/// <param name="MatchId"><see cref="WireMessageKind.SessionCreated"/>: the id the server assigned this match.</param>
/// <param name="Snapshot"><see cref="WireMessageKind.SessionCreated"/>: the match at tick 0.</param>
/// <param name="CommandId">
/// <see cref="WireMessageKind.Command"/> and <see cref="WireMessageKind.CommandResult"/>: correlates
/// a result to the command it answers. A result for an unrecognised id is a protocol error, not a
/// silently dropped message.
/// </param>
/// <param name="Command"><see cref="WireMessageKind.Command"/>: the command itself. Carries no player id (D-76) - the session attributes it to its own local player.</param>
/// <param name="CommandResult"><see cref="WireMessageKind.CommandResult"/>: whether it was applied.</param>
/// <param name="Events"><see cref="WireMessageKind.Events"/>: the delta since the client's last-known tick.</param>
/// <param name="SnapshotHash"><see cref="WireMessageKind.Events"/>: the hash of the snapshot this batch, applied, reproduces (D-71) - the client's own desync detector.</param>
/// <param name="Reason"><see cref="WireMessageKind.Error"/>: why.</param>
public sealed record WireMessage(
    WireMessageKind Kind,
    int ProtocolVersion,
    IReadOnlyList<string>? MapNames,
    string? MapName,
    long? TimeScale,
    string? MatchId,
    MatchSnapshot? Snapshot,
    int? CommandId,
    GatewayCommand? Command,
    GatewayCommandResult? CommandResult,
    EventBatch? Events,
    ulong? SnapshotHash,
    string? Reason)
{
    /// <summary>
    /// Which message this is. Validated at construction, so a malformed message deserialized off the
    /// wire is discovered where it arrives rather than at whichever field a dispatcher happens to
    /// dereference first (§5 - every inbound message is validated where it is deserialized).
    /// </summary>
    public WireMessageKind Kind { get; } = Validate(
        Kind, MapNames, MapName, TimeScale, MatchId, Snapshot, CommandId, Command, CommandResult, Events, SnapshotHash, Reason);

    /// <summary>A client's opening frame.</summary>
    public static WireMessage Hello(int protocolVersion) =>
        new(WireMessageKind.Hello, protocolVersion, null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>The server's reply to <see cref="Hello"/>.</summary>
    public static WireMessage Welcome(int protocolVersion, IReadOnlyList<string> mapNames) =>
        new(WireMessageKind.Welcome, protocolVersion, mapNames, null, null, null, null, null, null, null, null, null, null);

    /// <summary>A client's request to start a match.</summary>
    public static WireMessage CreateSession(int protocolVersion, string mapName, long timeScale) =>
        new(WireMessageKind.CreateSession, protocolVersion, null, mapName, timeScale, null, null, null, null, null, null, null, null);

    /// <summary>The server's reply to <see cref="CreateSession"/>.</summary>
    public static WireMessage SessionCreated(int protocolVersion, string matchId, MatchSnapshot snapshot) =>
        new(WireMessageKind.SessionCreated, protocolVersion, null, null, null, matchId, snapshot, null, null, null, null, null, null);

    /// <summary>A client's submitted command.</summary>
    public static WireMessage SubmitCommand(int protocolVersion, int commandId, GatewayCommand command) =>
        new(WireMessageKind.Command, protocolVersion, null, null, null, null, null, commandId, command, null, null, null, null);

    /// <summary>The server's verdict on a submitted command.</summary>
    public static WireMessage CommandResultFor(int protocolVersion, int commandId, GatewayCommandResult result) =>
        new(WireMessageKind.CommandResult, protocolVersion, null, null, null, null, null, commandId, null, result, null, null, null);

    /// <summary>The server's periodic delta.</summary>
    public static WireMessage EventsFor(int protocolVersion, EventBatch events, ulong snapshotHash) =>
        new(WireMessageKind.Events, protocolVersion, null, null, null, null, null, null, null, null, events, snapshotHash, null);

    /// <summary>A refusal, either direction.</summary>
    public static WireMessage ErrorFor(int protocolVersion, string reason) =>
        new(WireMessageKind.Error, protocolVersion, null, null, null, null, null, null, null, null, null, null, reason);

    private static WireMessageKind Validate(
        WireMessageKind kind,
        IReadOnlyList<string>? mapNames,
        string? mapName,
        long? timeScale,
        string? matchId,
        MatchSnapshot? snapshot,
        int? commandId,
        GatewayCommand? command,
        GatewayCommandResult? commandResult,
        EventBatch? events,
        ulong? snapshotHash,
        string? reason)
    {
        switch (kind)
        {
            case WireMessageKind.Hello:
                RequireAbsent(mapNames is null, nameof(mapNames), kind);
                RequireAbsent(mapName is null, nameof(mapName), kind);
                RequireAbsent(timeScale is null, nameof(timeScale), kind);
                RequireAbsent(matchId is null, nameof(matchId), kind);
                RequireAbsent(snapshot is null, nameof(snapshot), kind);
                RequireAbsent(commandId is null, nameof(commandId), kind);
                RequireAbsent(command is null, nameof(command), kind);
                RequireAbsent(commandResult is null, nameof(commandResult), kind);
                RequireAbsent(events is null, nameof(events), kind);
                RequireAbsent(snapshotHash is null, nameof(snapshotHash), kind);
                RequireAbsent(reason is null, nameof(reason), kind);
                return kind;

            case WireMessageKind.Welcome:
                RequirePresent(mapNames is not null, nameof(mapNames), kind);
                return kind;

            case WireMessageKind.CreateSession:
                RequirePresent(mapName is not null, nameof(mapName), kind);
                RequirePresent(timeScale is not null, nameof(timeScale), kind);
                return kind;

            case WireMessageKind.SessionCreated:
                RequirePresent(matchId is not null, nameof(matchId), kind);
                RequirePresent(snapshot is not null, nameof(snapshot), kind);
                return kind;

            case WireMessageKind.Command:
                RequirePresent(commandId is not null, nameof(commandId), kind);
                RequirePresent(command is not null, nameof(command), kind);
                return kind;

            case WireMessageKind.CommandResult:
                RequirePresent(commandId is not null, nameof(commandId), kind);
                RequirePresent(commandResult is not null, nameof(commandResult), kind);
                return kind;

            case WireMessageKind.Events:
                RequirePresent(events is not null, nameof(events), kind);
                RequirePresent(snapshotHash is not null, nameof(snapshotHash), kind);
                return kind;

            case WireMessageKind.Error:
                RequirePresent(!string.IsNullOrWhiteSpace(reason), nameof(reason), kind);
                return kind;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown wire message kind.");
        }
    }

    private static void RequirePresent(bool condition, string fieldName, WireMessageKind kind)
    {
        if (!condition)
        {
            throw new ArgumentException(FormattableString.Invariant($"A {kind} message must carry '{fieldName}'."), fieldName);
        }
    }

    private static void RequireAbsent(bool condition, string fieldName, WireMessageKind kind)
    {
        if (!condition)
        {
            throw new ArgumentException(FormattableString.Invariant($"A {kind} message carries no '{fieldName}'."), fieldName);
        }
    }
}
