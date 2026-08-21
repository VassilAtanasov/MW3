using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MW3.Server;

/// <summary>
/// Appends the per-match JSON Lines log a <see cref="MatchSession"/> opens at construction and
/// closes on <see cref="MatchSessionRegistry.Remove"/> (FR-6, D-86). One self-contained JSON object
/// per line - <c>header</c>, <c>command</c>, <c>event</c>, <c>hash</c>, <c>trailer</c>, or (once)
/// <c>truncated</c> - UTF-8, no BOM, LF endings, flushed after every write so a killed server loses
/// at most the record in flight. The full format is documented in
/// <c>docs/game-server/ARCHITECTURE.md</c> §4 (D-86..D-91).
///
/// Every public method swallows its own failures (D-87a): a disk error disables further logging for
/// this session but must never propagate into <see cref="MatchSession.TickAsync"/>, because
/// <see cref="TickScheduler.ExecuteAsync"/> would otherwise evict a perfectly healthy match over a
/// full disk. Once disabled - by a write failure, by the size cap (below), or because this session
/// was constructed with no log directory at all - every method is a silent no-op.
/// </summary>
internal sealed class MatchLogWriter : IDisposable
{
    /// <summary>Bumped when a record's shape changes; never for a value change.</summary>
    internal const int LogFormatVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly StreamWriter? _writer;

    private bool _disabled;
    private bool _disposed;
    private long _bytesWritten;
    private long _lastTick;

    private int _commandsAccepted;
    private int _commandsRejected;
    private int _brainCommands;
    private int _eventsRecorded;

    private readonly long _capBytes;

    private MatchLogWriter(StreamWriter? writer, long capBytes)
    {
        _writer = writer;
        _disabled = writer is null;
        _capBytes = capBytes;
    }

    /// <summary>
    /// Opens <c>&lt;matchId&gt;.jsonl</c> under <paramref name="logDirectory"/>. Returns a writer
    /// that silently no-ops on every call - never throws - when <paramref name="logDirectory"/> is
    /// null (tests exercising something other than logging) or when the file could not be opened: a
    /// session must start even if its own log cannot (D-87a).
    /// </summary>
    /// <param name="logDirectory">The directory to log to, or null to log nothing.</param>
    /// <param name="matchId">This match's id - the log file's base name.</param>
    /// <param name="capBytesOverride">
    /// A non-default size cap, for the FR-6 test that proves the cap's behaviour without writing
    /// <see cref="ServerTuning.LogSizeCapBytes"/> worth of records. Every production call site omits
    /// this and gets the real tuned value (D-22).
    /// </param>
    internal static MatchLogWriter Create(string? logDirectory, string matchId, long? capBytesOverride = null)
    {
        var capBytes = capBytesOverride ?? ServerTuning.LogSizeCapBytes;

        if (logDirectory is null)
        {
            return new MatchLogWriter(null, capBytes);
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, matchId + ".jsonl");
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                NewLine = "\n",
                AutoFlush = false,
            };
            return new MatchLogWriter(writer, capBytes);
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            return new MatchLogWriter(null, capBytes);
        }
    }

    /// <summary>The header, written before the session's first tick.</summary>
    internal void WriteHeader(string matchId, string? mapName, long timeScale, int localPlayerId, MatchSnapshot snapshot, ulong snapshotHash)
    {
        if (_disabled)
        {
            return;
        }

        _lastTick = snapshot.ElapsedTicks;
        TryWrite(() => new
        {
            kind = "header",
            logFormatVersion = LogFormatVersion,
            protocolVersion = MatchSnapshot.CurrentProtocolVersion,
            matchId,
            mapName,
            timeScale,
            localPlayerId,
            snapshot,
            snapshotHash,
            timestampUtc = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// A client-submitted command, with the verdict <see cref="MatchSession.ApplyCommand"/> already
    /// holds (D-90). <paramref name="sendUnitCount"/> is the exact unit count a
    /// <see cref="GatewayCommandKind.SendArmy"/> actually committed, or null for every other kind -
    /// carried explicitly (D-89) so the replay reader applies the same count rather than recomputing
    /// one from <see cref="GatewayCommand.Strength"/> against whatever garrison its own replayed
    /// state happens to hold at that instant, which is guaranteed to agree only at hash-checked ticks.
    /// </summary>
    internal void WriteClientCommand(long tick, int playerId, GatewayCommand command, GatewayCommandResult result, int? sendUnitCount)
    {
        if (_disabled)
        {
            return;
        }

        _lastTick = tick;
        var written = TryWrite(() => new
        {
            kind = "command",
            tick,
            source = "client",
            playerId,
            command,
            sendUnitCount,
            accepted = result.Accepted,
            rejectionReason = result.RejectionReason,
        });

        if (written)
        {
            if (result.Accepted)
            {
                _commandsAccepted++;
            }
            else
            {
                _commandsRejected++;
            }
        }
    }

    /// <summary>
    /// A brain-decided command (D-87), carrying no verdict - see <see cref="LoggingPlayerBrain"/>.
    /// <paramref name="sendUnitCount"/> is the exact unit count the brain's own
    /// <c>SendArmyCommand</c> carried when it is a send, null otherwise - see
    /// <see cref="WriteClientCommand"/> for why this is logged rather than left to be recomputed.
    /// </summary>
    internal void WriteBrainCommand(long tick, int playerId, GatewayCommand command, int? sendUnitCount)
    {
        if (_disabled)
        {
            return;
        }

        _lastTick = tick;
        var written = TryWrite(() => new
        {
            kind = "command",
            tick,
            source = "brain",
            playerId,
            command,
            sendUnitCount,
        });

        if (written)
        {
            _brainCommands++;
        }
    }

    /// <summary>One curated, derived event (D-88) - callers filter with <see cref="LoggedEventFilter"/> before calling this.</summary>
    internal void WriteEvent(long tick, MatchEvent matchEvent)
    {
        if (_disabled)
        {
            return;
        }

        _lastTick = tick;
        var written = TryWrite(() => new
        {
            kind = "event",
            tick,
            @event = matchEvent,
        });

        if (written)
        {
            _eventsRecorded++;
        }
    }

    /// <summary>A periodic snapshot hash, at the interval REQUIREMENTS §4 names (<see cref="ServerTuning.LogHashIntervalTicks"/>).</summary>
    internal void WriteHash(long tick, ulong hash)
    {
        if (_disabled)
        {
            return;
        }

        _lastTick = tick;
        TryWrite(() => new
        {
            kind = "hash",
            tick,
            hash,
        });
    }

    /// <summary>
    /// The trailer, written synchronously and without awaiting (D-87a: <see cref="MatchSession.Dispose"/>
    /// is a plain <see cref="IDisposable"/>), then closes the file. A no-op once the size cap has
    /// already truncated this log - the trailer is part of what "stops appending" stops.
    /// </summary>
    internal void WriteTrailerAndClose(long tick, MatchOutcome outcome, ulong finalHash)
    {
        if (!_disabled)
        {
            _lastTick = tick;
            TryWrite(() => new
            {
                kind = "trailer",
                tick,
                outcome,
                finalHash,
                commandsAccepted = _commandsAccepted,
                commandsRejected = _commandsRejected,
                brainCommands = _brainCommands,
                eventsRecorded = _eventsRecorded,
                timestampUtc = DateTime.UtcNow,
            });
        }

        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // Closing is best-effort too (D-87a) - a failure here must not propagate either.
        }
    }

    /// <summary>
    /// Serializes and appends one record, enforcing the per-match size cap
    /// (<see cref="ServerTuning.LogSizeCapBytes"/>) before writing it. Returns whether the record
    /// (the caller's, not a resulting <c>truncated</c> marker) was actually appended, so callers can
    /// gate their own bookkeeping (accepted/rejected/brain/event counts) on real writes only.
    /// </summary>
    private bool TryWrite(Func<object> buildRecord)
    {
        if (_disabled || _writer is null)
        {
            return false;
        }

        try
        {
            var json = JsonSerializer.Serialize(buildRecord(), _jsonOptions);
            var byteCount = Encoding.UTF8.GetByteCount(json) + 1; // + the LF this method appends.

            if (_bytesWritten + byteCount > _capBytes)
            {
                WriteTruncatedRecordUnchecked();
                return false;
            }

            _writer.Write(json);
            _writer.Write('\n');
            _writer.Flush();
            _bytesWritten += byteCount;
            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            _disabled = true;
            return false;
        }
    }

    /// <summary>
    /// Writes the single <c>truncated</c> record and disables further logging (never a failure - the
    /// match keeps playing, §6's last bullet). Bypasses the cap check <see cref="TryWrite"/> itself
    /// performs, since a small fixed-shape marker record always fits the headroom the cap leaves.
    /// </summary>
    private void WriteTruncatedRecordUnchecked()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new { kind = "truncated", tick = _lastTick, capBytes = _capBytes },
                _jsonOptions);
            _writer!.Write(json);
            _writer.Write('\n');
            _writer.Flush();
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // Even the truncation marker can fail to write - still never propagates (D-87a).
        }
        finally
        {
            _disabled = true;
        }
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException or ObjectDisposedException;
}
