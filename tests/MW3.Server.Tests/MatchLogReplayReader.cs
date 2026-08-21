using System.Text.Json;
using MW3.Core;

namespace MW3.Server.Tests;

/// <summary>
/// The minimal, test-only replay reader D-89 asks for: rebuilds a fresh <see cref="Match"/> from a
/// logged header alone, re-applies the logged <c>accepted</c> client commands and every
/// <c>brain</c> command at their logged ticks in logged order, and reports the result so a test can
/// compare it against the log's own <c>hash</c> and <c>trailer</c> records. Deliberately lives here,
/// not in a shipped project - shipping a reader is the <b>Game logs, game replays</b> project's
/// content, and this one exists only to prove the format sufficient.
/// </summary>
internal static class MatchLogReplayReader
{
    internal readonly record struct ReplayResult(
        ulong FinalHash,
        MatchOutcome Outcome,
        long FinalTick,
        IReadOnlyList<HashCheck> HashChecks);

    /// <summary>One logged <c>hash</c> record, and the hash the replay itself computed at that same tick.</summary>
    internal readonly record struct HashCheck(long Tick, ulong LoggedHash, ulong ReplayHash);

    internal static ReplayResult Replay(string logPath)
    {
        var lines = MatchLogReader.ReadLines(logPath);
        if (lines.Count == 0 || lines[0].Kind != "header")
        {
            throw new InvalidOperationException("Log has no header - nothing to replay from.");
        }

        var header = lines[0].Root;
        var mapId = Enum.Parse<MapId>(header.GetProperty("mapName").GetString()!, ignoreCase: true);
        var localPlayerId = header.GetProperty("localPlayerId").GetInt32();

        var match = new Match(MapCatalog.Get(mapId));
        var localPlayer = localPlayerId == match.HumanPlayer.Id ? match.HumanPlayer : match.AiPlayer;
        var otherPlayer = localPlayer.Id == match.HumanPlayer.Id ? match.AiPlayer : match.HumanPlayer;

        var currentTick = 0L;
        var hashChecks = new List<HashCheck>();
        var finalHash = 0UL;
        var outcome = MatchOutcome.InProgress;
        var finalTick = 0L;

        for (var i = 1; i < lines.Count; i++)
        {
            var (kind, root) = lines[i];
            switch (kind)
            {
                case "command":
                    var tick = root.GetProperty("tick").GetInt64();
                    var source = root.GetProperty("source").GetString();

                    // D-90: replay skips anything that is not an accepted client command or a brain
                    // command - a rejected command changed nothing and is not input.
                    if (source == "client" && !root.GetProperty("accepted").GetBoolean())
                    {
                        break;
                    }

                    AdvanceTo(match, ref currentTick, tick);
                    var playerId = root.GetProperty("playerId").GetInt32();
                    var player = playerId == localPlayer.Id ? localPlayer : otherPlayer;

                    // D-89: a SendArmy replays at the exact unit count the log recorded, never
                    // recomputed from Strength against the replay's own garrison - the two matches
                    // are proven to agree only at hash-checked ticks, not at every tick along the way.
                    var exactSendUnitCount = root.TryGetProperty("sendUnitCount", out var countEl) && countEl.ValueKind != JsonValueKind.Null
                        ? countEl.GetInt32()
                        : (int?)null;

                    GatewayCommandApplier.Apply(match, player, ParseCommand(root.GetProperty("command")), exactSendUnitCount);
                    break;

                case "hash":
                    var hashTick = root.GetProperty("tick").GetInt64();
                    AdvanceTo(match, ref currentTick, hashTick);
                    var loggedHash = root.GetProperty("hash").GetUInt64();
                    var replayHash = SnapshotHash.Compute(MatchSnapshotBuilder.Build(match, localPlayer));
                    hashChecks.Add(new HashCheck(hashTick, loggedHash, replayHash));
                    break;

                case "trailer":
                    finalTick = root.GetProperty("tick").GetInt64();
                    AdvanceTo(match, ref currentTick, finalTick);
                    finalHash = root.GetProperty("finalHash").GetUInt64();
                    outcome = Enum.Parse<MatchOutcome>(root.GetProperty("outcome").GetString()!, ignoreCase: true);
                    break;

                    // "event" records are derived, not replay input (D-88) - skipped entirely.
            }
        }

        return new ReplayResult(finalHash, outcome, finalTick, hashChecks);
    }

    /// <summary>
    /// Advances <paramref name="match"/> from <paramref name="currentTick"/> to
    /// <paramref name="targetTick"/> in the same <see cref="MatchRunner.DecisionIntervalTicks"/>-sized
    /// steps <see cref="MatchRunner.Advance"/> itself takes (crossing every decision boundary in its
    /// own <c>Match.Advance</c> call, exactly like production) rather than one large jump straight to
    /// the target, and always follows with one zero-length <c>Match.Advance(0)</c> call.
    ///
    /// That trailing call matters and is not a no-op: <see cref="MatchRunner.Advance"/>'s own loop
    /// always ends with a call to <c>Match.Advance</c> for whatever ticks remain in its budget - which
    /// is legitimately zero whenever a decision lands exactly on the outer call's target tick (true on
    /// every beat once <c>TimeScale</c> is a multiple of <see cref="MatchRunner.DecisionIntervalTicks"/>,
    /// which every timescale this suite uses is). A freshly-launched army's own tower-fire evaluation
    /// on a Big-map path within range happens on that trailing call, so skipping it when the target is
    /// already reached reproduces a different final state than production - found by a Big-map replay
    /// diverging on an army's unit count alone, with every base's own state still bit-identical.
    /// </summary>
    private static void AdvanceTo(Match match, ref long currentTick, long targetTick)
    {
        while (targetTick > currentTick)
        {
            var nextBoundary = ((currentTick / MatchRunner.DecisionIntervalTicks) + 1) * MatchRunner.DecisionIntervalTicks;
            var step = Math.Min(nextBoundary, targetTick) - currentTick;
            match.Advance(step);
            currentTick += step;
        }

        match.Advance(0);
    }

    private static GatewayCommand ParseCommand(JsonElement element)
    {
        var kind = Enum.Parse<GatewayCommandKind>(element.GetProperty("kind").GetString()!, ignoreCase: true);
        var fromBaseId = element.GetProperty("fromBaseId").GetInt32();

        switch (kind)
        {
            case GatewayCommandKind.SendArmy:
                var toBaseId = element.GetProperty("toBaseId").GetInt32();
                var strength = Enum.Parse<SendStrength>(element.GetProperty("strength").GetString()!, ignoreCase: true);
                return GatewayCommand.SendArmy(fromBaseId, toBaseId, strength);

            case GatewayCommandKind.Upgrade:
                return GatewayCommand.Upgrade(fromBaseId);

            case GatewayCommandKind.Convert:
                var targetType = Enum.Parse<BaseType>(element.GetProperty("targetType").GetString()!, ignoreCase: true);
                return GatewayCommand.Convert(fromBaseId, targetType);

            default:
                throw new InvalidOperationException($"Unknown logged command kind: {kind}.");
        }
    }
}
