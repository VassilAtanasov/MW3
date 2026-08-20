using System.Net.WebSockets;
using MW3.Core;
using MW3.Transport;

namespace MW3.Server;

/// <summary>
/// One match, entirely self-contained: a <see cref="Match"/>, a <see cref="MatchRunner"/> with a
/// fresh <see cref="AiBrain"/>, its command inbox, its last-sent snapshot, its connection and its
/// match id (§"MW3.Server"). Two sessions share nothing - no statics, no ambient clock - which is
/// what makes <see cref="TickScheduler"/>'s single 50 ms hosted service (D-63) safe to walk every
/// live session without any cross-session lock.
/// </summary>
internal sealed class MatchSession : IDisposable
{
    private readonly AiBrain _aiOpponentBrain;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>Active only after the disconnect grace period expires (D-65) - null while the human is connected or before the grace has elapsed.</summary>
    private AiBrain? _humanSubstituteBrain;

    private long _lastSentTick;

    internal MatchSession(string matchId, MapDefinition definition, long timeScale, WebSocket connection)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(connection);
        if (timeScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be positive.");
        }

        MatchId = matchId;
        TimeScale = timeScale;
        Match = new Match(definition);
        _aiOpponentBrain = new AiBrain(Match.AiPlayer);
        Runner = new MatchRunner(Match, _aiOpponentBrain);
        Connection = connection;

        LastSentSnapshot = MatchSnapshotBuilder.Build(Match, Match.HumanPlayer);
        _lastSentTick = LastSentSnapshot.ElapsedTicks;
    }

    internal string MatchId { get; }

    internal long TimeScale { get; }

    internal Match Match { get; }

    internal MatchRunner Runner { get; }

    /// <summary>The current connection, or null once it has closed. There is no reconnect (§6 - out of scope).</summary>
    internal WebSocket? Connection { get; private set; }

    /// <summary>The snapshot as of the last <see cref="WireMessageKind.Events"/> sent - what the client is assumed to hold.</summary>
    internal MatchSnapshot LastSentSnapshot { get; private set; }

    /// <summary>Commands received but not yet applied - drained at the top of every tick (D-59: a command applies when it arrives).</summary>
    internal System.Collections.Concurrent.ConcurrentQueue<(int CommandId, GatewayCommand Command)> Inbox { get; } = new();

    /// <summary>Scheduler beats (not sim ticks) since the connection closed. Reset by nothing - there is no reconnect.</summary>
    internal long DisconnectedBeats { get; private set; }

    /// <summary>
    /// Evicted when the match is decided and abandoned, or after the idle timeout with no connection
    /// attached at all - whichever comes first (§"MW3.Server" lifecycle bullet).
    /// </summary>
    internal bool ShouldEvict =>
        (Connection is null && Match.Outcome != MatchOutcome.InProgress)
        || DisconnectedBeats >= ServerTuning.IdleEvictionTicks;

    /// <summary>Marks the connection closed. Idempotent.</summary>
    internal void Disconnect() => Connection = null;

    /// <inheritdoc />
    public void Dispose() => _sendGate.Dispose();

    /// <summary>
    /// One scheduler beat (D-63): drain the inbox, advance the match, substitute the AI for a player
    /// missing past its grace period (D-65), and send an events batch if one is due and a connection
    /// is attached.
    /// </summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        await DrainInboxAsync(cancellationToken).ConfigureAwait(false);

        if (Match.Outcome != MatchOutcome.InProgress)
        {
            await FlushEventsIfDueAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Connection is null)
        {
            DisconnectedBeats++;
            if (DisconnectedBeats == ServerTuning.DisconnectGraceTicks && _humanSubstituteBrain is null)
            {
                // D-65: the AI runs server-side and MatchRunner already consults an IPlayerBrain, so
                // substituting one for the missing human is swapping an implementation, not new
                // machinery - this AiBrain just happens to decide for Match.HumanPlayer.
                _humanSubstituteBrain = new AiBrain(Match.HumanPlayer);
            }
        }

        var ticksBefore = Match.ElapsedTicks;
        Runner.Advance(TimeScale);
        ExecuteSubstituteDecisionsIfAny(ticksBefore, Match.ElapsedTicks);

        await FlushEventsIfDueAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Mirrors <see cref="MatchRunner.Advance"/>'s own decision-tick cadence for the human
    /// substitute, which <see cref="MatchRunner"/> itself does not know about - it was built for
    /// exactly one AI brain. Runs after <see cref="MatchRunner.Advance"/> so the opponent AI's own
    /// decision at the same boundary is unaffected.
    /// </summary>
    private void ExecuteSubstituteDecisionsIfAny(long ticksBefore, long ticksAfter)
    {
        if (_humanSubstituteBrain is null)
        {
            return;
        }

        var firstBoundary = ((ticksBefore / MatchRunner.DecisionIntervalTicks) + 1) * MatchRunner.DecisionIntervalTicks;
        for (var boundary = firstBoundary; boundary <= ticksAfter; boundary += MatchRunner.DecisionIntervalTicks)
        {
            if (Match.Outcome != MatchOutcome.InProgress)
            {
                return;
            }

            var decision = _humanSubstituteBrain.Decide(Match);
            if (!decision.HasCommand)
            {
                continue;
            }

            if (decision.IsUpgrade)
            {
                Match.Execute(decision.Upgrade);
            }
            else if (decision.IsConvert)
            {
                Match.Execute(decision.Convert);
            }
            else
            {
                Match.Execute(decision.Command);
            }
        }
    }

    private async Task DrainInboxAsync(CancellationToken cancellationToken)
    {
        while (Inbox.TryDequeue(out var pending))
        {
            var result = ApplyCommand(pending.Command);
            await SendAsync(
                WireMessage.CommandResultFor(MatchSnapshot.CurrentProtocolVersion, pending.CommandId, result),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies one command on behalf of the connected client's local player - always
    /// <see cref="Match.HumanPlayer"/> (D-76: a gateway command carries no issuing player). Mirrors
    /// <c>LoopbackMatchGateway.Submit</c> field for field: same command shape, same outcome mapping,
    /// which is what makes this "the same predicate the snapshot builder uses" (D-66) rather than a
    /// second copy of the rule.
    /// </summary>
    private GatewayCommandResult ApplyCommand(GatewayCommand command)
    {
        switch (command.Kind)
        {
            case GatewayCommandKind.SendArmy:
                var source = FindBase(command.FromBaseId);
                if (source is null)
                {
                    return GatewayCommandResult.Rejected(SendArmyOutcome.BaseNotFound.ToString());
                }

                var unitCount = SendStrengthCalculator.Compute(source.GarrisonCount, command.Strength!.Value);
                return Describe(
                    Runner.Execute(new SendArmyCommand(Match.HumanPlayer, command.FromBaseId, command.ToBaseId!.Value, unitCount)),
                    SendArmyOutcome.Accepted);

            case GatewayCommandKind.Upgrade:
                return Describe(Runner.Execute(new UpgradeCommand(Match.HumanPlayer, command.FromBaseId)), UpgradeOutcome.Accepted);

            case GatewayCommandKind.Convert:
                return Describe(
                    Runner.Execute(new ConvertCommand(Match.HumanPlayer, command.FromBaseId, command.TargetType!.Value)),
                    ConvertOutcome.Accepted);

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown gateway command kind.");
        }
    }

    private static GatewayCommandResult Describe<TOutcome>(TOutcome outcome, TOutcome accepted)
        where TOutcome : struct, Enum =>
        EqualityComparer<TOutcome>.Default.Equals(outcome, accepted)
            ? GatewayCommandResult.Ok()
            : GatewayCommandResult.Rejected(outcome.ToString()!);

    private Base? FindBase(int id)
    {
        var bases = Match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == id)
            {
                return bases[i];
            }
        }

        return null;
    }

    /// <summary>
    /// True if <paramref name="baseId"/> names a base in this match - the boundary check a
    /// <c>Command</c> message's base ids are validated against before the command ever
    /// reaches the inbox (§"Every inbound message is validated where it is deserialized").
    /// </summary>
    internal bool BaseExists(int baseId) => FindBase(baseId) is not null;

    private async Task FlushEventsIfDueAsync(CancellationToken cancellationToken)
    {
        if (Connection is null)
        {
            return;
        }

        var due = Match.ElapsedTicks - _lastSentTick >= ServerTuning.SendIntervalTicks;
        var justConcluded = Match.Outcome != MatchOutcome.InProgress && LastSentSnapshot.Outcome == MatchOutcome.InProgress;
        if (!due && !justConcluded)
        {
            return;
        }

        var built = MatchSnapshotBuilder.Build(Match, Match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(LastSentSnapshot, built);
        var applied = SnapshotApplier.Apply(batch, LastSentSnapshot);
        var hash = SnapshotHash.Compute(applied);

        LastSentSnapshot = applied;
        _lastSentTick = applied.ElapsedTicks;

        await SendAsync(WireMessage.EventsFor(MatchSnapshot.CurrentProtocolVersion, batch, hash), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one message, serialized against concurrent sends from a command result and an events flush landing in the same beat.</summary>
    internal async Task SendAsync(WireMessage message, CancellationToken cancellationToken)
    {
        var connection = Connection;
        if (connection is null || connection.State != WebSocketState.Open)
        {
            return;
        }

        var codec = new JsonWireCodec();
        var bytes = codec.Encode(message);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection.State == WebSocketState.Open)
            {
                await WebSocketFraming.SendAsync(connection, bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException)
        {
            Disconnect();
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
