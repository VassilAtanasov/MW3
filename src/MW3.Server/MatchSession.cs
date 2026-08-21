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
    private readonly MatchLogWriter _logWriter;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    // Written by the WebSocket receive loop's thread (Disconnect()), read by the TickScheduler's
    // thread (TickAsync, FlushEventsIfDueAsync, SendAsync) - volatile for the same reason
    // RemoteMatchGateway's CurrentSnapshot is: a single reference write needs no lock to be safe
    // from a torn or stale read, but it does need the JIT/CPU barred from reordering or caching it.
    private volatile WebSocket? _connection;

    /// <summary>Active only after the disconnect grace period expires (D-65) - null while the human is connected or before the grace has elapsed.</summary>
    private LoggingPlayerBrain? _humanSubstituteBrain;

    private long _lastSentTick;
    private long _lastLoggedHashTick;

    /// <param name="matchId">This match's id, and the base name of its log file.</param>
    /// <param name="definition">The map this match is played on.</param>
    /// <param name="timeScale">Simulation ticks advanced per scheduler beat.</param>
    /// <param name="connection">The connected client's socket.</param>
    /// <param name="logDirectory">
    /// Where this session's <c>&lt;matchId&gt;.jsonl</c> log is written (FR-6, D-86), or null to log
    /// nothing - the shape most of this suite's pre-existing lifecycle tests use, since they are not
    /// exercising logging.
    /// </param>
    /// <param name="logSizeCapBytesOverride">
    /// A non-default per-match log size cap, for the FR-6 test that proves the cap's behaviour
    /// without writing <see cref="ServerTuning.LogSizeCapBytes"/> worth of records. Production never
    /// passes this (D-22).
    /// </param>
    internal MatchSession(
        string matchId,
        MapDefinition definition,
        long timeScale,
        WebSocket connection,
        string? logDirectory = null,
        long? logSizeCapBytesOverride = null)
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
        _connection = connection;

        LastSentSnapshot = MatchSnapshotBuilder.Build(Match, Match.HumanPlayer);
        _lastSentTick = LastSentSnapshot.ElapsedTicks;

        // Opened and the header written before this session's first tick, so a log that exists at
        // all always starts with a complete header (§"Format").
        _logWriter = MatchLogWriter.Create(logDirectory, matchId, logSizeCapBytesOverride);
        _logWriter.WriteHeader(matchId, LastSentSnapshot.MapId, timeScale, Match.HumanPlayer.Id, LastSentSnapshot, SnapshotHash.Compute(LastSentSnapshot));

        // D-87: the opponent AI's commands go straight to Match.Execute inside MatchRunner.Advance,
        // so this is the only way to observe them.
        Runner = new MatchRunner(Match, new LoggingPlayerBrain(new AiBrain(Match.AiPlayer), _logWriter));
    }

    internal string MatchId { get; }

    internal long TimeScale { get; }

    internal Match Match { get; }

    internal MatchRunner Runner { get; }

    /// <summary>The current connection, or null once it has closed. There is no reconnect (§6 - out of scope).</summary>
    internal WebSocket? Connection => _connection;

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
    internal void Disconnect() => _connection = null;

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            // Synchronous and without awaiting (D-87a: MatchSessionRegistry.Remove calls this
            // synchronous IDisposable). A failure building the final snapshot must not stop the
            // session from being disposed - the writer's own methods already guard their own I/O,
            // but building the snapshot happens out here.
            var finalSnapshot = MatchSnapshotBuilder.Build(Match, Match.HumanPlayer);
            _logWriter.WriteTrailerAndClose(Match.ElapsedTicks, Match.Outcome, SnapshotHash.Compute(finalSnapshot));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // D-87a: a logging failure must never prevent a session from being disposed.
            _logWriter.Dispose();
        }
        finally
        {
            _sendGate.Dispose();
        }
    }

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
                // machinery - this AiBrain just happens to decide for Match.HumanPlayer. Wrapped here,
                // at the point it is constructed - D-87a: the substitute is lazy, so wrapping only in
                // the constructor would miss exactly the abandoned-match stretch this feature exists
                // to record.
                _humanSubstituteBrain = new LoggingPlayerBrain(new AiBrain(Match.HumanPlayer), _logWriter);
            }
        }

        AdvanceInterleavingSubstitute(TimeScale);

        await FlushEventsIfDueAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the match by <paramref name="ticks"/>, consulting the human substitute (when active)
    /// at exactly the same decision boundaries <see cref="MatchRunner.Advance"/> already stops the
    /// opponent AI at - never against state <see cref="MatchRunner.Advance"/> has already carried
    /// past that boundary. A first cut delegated the whole beat to one <c>Runner.Advance(ticks)</c>
    /// call and replayed the substitute's decisions afterward by tick range; that reads the same but
    /// is wrong whenever a beat crosses more than one boundary (any time scale above
    /// <see cref="MatchRunner.DecisionIntervalTicks"/>, which is what every multi-session test in
    /// this suite uses) - every replayed decision would see the match already advanced to the beat's
    /// end rather than to its own boundary. Stepping to each boundary one at a time, exactly as
    /// <see cref="MatchRunner.Advance"/>'s own internal loop does, keeps both brains looking at the
    /// same tick they would in a single-boundary beat.
    /// </summary>
    private void AdvanceInterleavingSubstitute(long ticks)
    {
        if (_humanSubstituteBrain is null)
        {
            Runner.Advance(ticks);
            return;
        }

        var targetElapsedTicks = Match.ElapsedTicks + ticks;
        while (true)
        {
            if (Match.Outcome != MatchOutcome.InProgress)
            {
                return;
            }

            var nextDecisionTick = ((Match.ElapsedTicks / MatchRunner.DecisionIntervalTicks) + 1) * MatchRunner.DecisionIntervalTicks;
            if (nextDecisionTick > targetElapsedTicks)
            {
                Runner.Advance(targetElapsedTicks - Match.ElapsedTicks);
                return;
            }

            // Carries the match to exactly one boundary - the opponent AI decides inside this call,
            // at that same tick, the same way it always has.
            Runner.Advance(nextDecisionTick - Match.ElapsedTicks);

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
            var applied = ApplyCommand(pending.Command);

            // D-89, D-90: the one clean hook for the client half of the log - the verdict is already
            // in hand, on this thread, before it is sent. The exact unit count a SendArmy actually
            // committed is logged alongside it (D-89) rather than left for the replay reader to
            // recompute from Strength against whatever garrison its own replayed Match happens to
            // hold at that instant - the two are only proven to agree at hash-checked ticks, not at
            // every tick along the way.
            _logWriter.WriteClientCommand(Match.ElapsedTicks, Match.HumanPlayer.Id, pending.Command, applied.Result, applied.SendUnitCount);

            await SendAsync(
                WireMessage.CommandResultFor(MatchSnapshot.CurrentProtocolVersion, pending.CommandId, applied.Result),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies one command on behalf of the connected client's local player - always
    /// <see cref="Match.HumanPlayer"/> (D-76: a gateway command carries no issuing player). Delegates
    /// to <see cref="GatewayCommandApplier"/>, the one translation from the wire shape to the rules'
    /// own commands shared with the FR-6 replay-equivalence test's reader.
    /// </summary>
    private GatewayCommandApplier.ApplyResult ApplyCommand(GatewayCommand command) => GatewayCommandApplier.Apply(Match, Match.HumanPlayer, command);

    /// <summary>
    /// True if <paramref name="baseId"/> names a base in this match - the boundary check a
    /// <c>Command</c> message's base ids are validated against before the command ever
    /// reaches the inbox (§"Every inbound message is validated where it is deserialized").
    /// </summary>
    internal bool BaseExists(int baseId) => GatewayCommandApplier.FindBase(Match, baseId) is not null;

    private async Task FlushEventsIfDueAsync(CancellationToken cancellationToken)
    {
        var due = Match.ElapsedTicks - _lastSentTick >= ServerTuning.SendIntervalTicks;
        var justConcluded = Match.Outcome != MatchOutcome.InProgress && LastSentSnapshot.Outcome == MatchOutcome.InProgress;
        if (!due && !justConcluded)
        {
            return;
        }

        var previous = LastSentSnapshot;
        var built = MatchSnapshotBuilder.Build(Match, Match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(previous, built);
        var applied = SnapshotApplier.Apply(batch, previous);
        var hash = SnapshotHash.Compute(applied);

        LastSentSnapshot = applied;
        _lastSentTick = applied.ElapsedTicks;

        // D-87a: recording is unconditional - sending, below, is gated on a connection; recording is
        // not, or the log would go silent from the moment a client disconnects.
        LogEventsAndHash(previous, batch, hash);

        if (Connection is null)
        {
            return;
        }

        await SendAsync(WireMessage.EventsFor(MatchSnapshot.CurrentProtocolVersion, batch, hash), cancellationToken).ConfigureAwait(false);
    }

    private void LogEventsAndHash(MatchSnapshot before, EventBatch batch, ulong hash)
    {
        var events = batch.Events;
        for (var i = 0; i < events.Count; i++)
        {
            var matchEvent = events[i];
            if (LoggedEventFilter.ShouldLog(matchEvent, before))
            {
                _logWriter.WriteEvent(batch.ToTick, matchEvent);
            }
        }

        if (batch.ToTick - _lastLoggedHashTick >= ServerTuning.LogHashIntervalTicks)
        {
            _logWriter.WriteHash(batch.ToTick, hash);
            _lastLoggedHashTick = batch.ToTick;
        }
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
