using System.Net.WebSockets;
using System.Text.Json;
using MW3.Core;

namespace MW3.Server.Tests;

/// <summary>
/// FR-6: the per-match JSON Lines log a <see cref="MatchSession"/> opens at construction and closes
/// on eviction. Drives <see cref="MatchSession"/> directly (internal, via
/// <c>InternalsVisibleTo</c>) rather than through a socket, the same way
/// <see cref="MatchSessionUnitTests"/> forces disconnect grace and eviction on demand, plus one
/// real two-session run through <see cref="ServerFixture"/> for the concurrency claim.
/// </summary>
public sealed class MatchLogTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "mw3-log-tests-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            try
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort - a session left mid-tick by a test can still hold its file open.
            }
        }
    }

    private string LogPathFor(string matchId) => Path.Combine(_logDirectory, matchId + ".jsonl");

    [Fact]
    public async Task BothBrains_AreLogged_PastTheDisconnectTick()
    {
        var matchId = "both-brains";
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 1, stub, _logDirectory);
        session.Disconnect();

        for (var beat = 0; beat < 200_000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        session.Dispose();

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        var brainCommands = lines.Where(l => l.Kind == "command" && l.Root.GetProperty("source").GetString() == "brain").ToList();

        var aiPlayerId = 2;
        var humanPlayerId = 1;

        Assert.Contains(brainCommands, c => c.Root.GetProperty("playerId").GetInt32() == aiPlayerId);
        Assert.Contains(
            brainCommands,
            c => c.Root.GetProperty("playerId").GetInt32() == humanPlayerId
                && c.Root.GetProperty("tick").GetInt64() > ServerTuning.DisconnectGraceTicks);
    }

    [Fact]
    public async Task ARejectedClientCommand_IsLoggedWithItsReason()
    {
        var matchId = "rejected-command";
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 1, stub, _logDirectory);

        // Base 1 is the AI's start base - the human does not own it, so this is rejected.
        session.Inbox.Enqueue((1, GatewayCommand.SendArmy(from: 1, to: 0, SendStrength.Half)));
        await session.TickAsync(CancellationToken.None);
        session.Dispose();

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        var rejected = lines.Single(l => l.Kind == "command" && l.Root.GetProperty("source").GetString() == "client");

        Assert.False(rejected.Root.GetProperty("accepted").GetBoolean());
        Assert.Equal(nameof(SendArmyOutcome.SourceNotOwnedByIssuer), rejected.Root.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task ExcludedEventKinds_AreNeverLogged()
    {
        var matchId = "excluded-events";
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 5000, stub, _logDirectory);

        for (var beat = 0; beat < 2000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        session.Dispose();

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        var eventKinds = lines
            .Where(l => l.Kind == "event")
            .Select(l => l.Root.GetProperty("event").GetProperty("kind").GetString())
            .ToList();

        // A busy Small-map match at this time scale reliably produces base and army changes, so a
        // suite that logged them wholesale would show up here - proving the filter is active, not
        // merely untested because nothing of the excluded kinds ever happened.
        Assert.NotEmpty(eventKinds);
        Assert.DoesNotContain("baseChanged", eventKinds);
        Assert.DoesNotContain("armyChanged", eventKinds);
        Assert.DoesNotContain("availableActionsChanged", eventKinds);
    }

    [Fact]
    public async Task Recording_ContinuesPastADisconnect_WithNoConnectionAttachedAtAll()
    {
        var matchId = "no-connection-recording";
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 1, stub, _logDirectory);
        session.Disconnect();

        for (var beat = 0; beat < ServerTuning.DisconnectGraceTicks + 500; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        session.Dispose();

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));

        // Both a hash record and an event past the disconnect tick prove FlushEventsIfDueAsync's
        // recording is not gated on Connection - only sending is.
        Assert.Contains(lines, l => l.Kind == "hash" && l.Root.GetProperty("tick").GetInt64() > ServerTuning.DisconnectGraceTicks);
    }

    [Fact]
    public async Task TrailerIsWritten_AndTheFileClosed_OnEviction_WithoutAwaiting()
    {
        var matchId = "trailer-on-eviction";
        var registry = new MatchSessionRegistry();
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 5000, stub, _logDirectory);

        registry.TryAdd(session);
        session.Disconnect();

        for (var beat = 0; beat < 5000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        Assert.True(session.ShouldEvict);

        // Registry.Remove calls the synchronous IDisposable - no await anywhere in this call. The
        // trailing `using` above disposing the same (already-disposed, idempotent) session is just
        // this test's own cleanup, not part of what is under test.
        registry.Remove(matchId);

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        Assert.True(MatchLogReader.IsComplete(lines));

        // The file is actually closed - a second reader can open it exclusively.
        using var exclusive = File.Open(LogPathFor(matchId), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Fact]
    public async Task AWriterThatCannotOpen_NeverEndsTheMatch_AndTheSessionIsNotEvictedEarly()
    {
        // A log directory that cannot be created (its parent is a plain file, not a directory) is a
        // realistic write failure MatchLogWriter.Create must absorb (D-87a) - the resulting no-op
        // writer must not affect the match at all.
        var unusableParent = Path.Combine(_logDirectory, "not-a-directory");
        Directory.CreateDirectory(_logDirectory);
        await File.WriteAllTextAsync(unusableParent, "not a directory");
        var unusableLogDirectory = Path.Combine(unusableParent, "logs");

        using var stub = new ClientWebSocket();
        using var session = new MatchSession("writer-cannot-open", MapCatalog.Get(MapId.Small), timeScale: 5000, stub, unusableLogDirectory);
        session.Disconnect();

        for (var beat = 0; beat < 5000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        Assert.NotEqual(MatchOutcome.InProgress, session.Match.Outcome);
        Assert.False(File.Exists(Path.Combine(unusableLogDirectory, "writer-cannot-open.jsonl")));
    }

    [Fact]
    public async Task ExceedingTheSizeCap_WritesASingleTruncatedRecord_AndTheMatchContinues()
    {
        var matchId = "size-cap";
        using var stub = new ClientWebSocket();

        // A tiny cap - a handful of header/hash/event records already exceed it, forcing the
        // truncation path on the very first beat or two without writing megabytes in a unit test.
        using var session = new MatchSession(
            matchId, MapCatalog.Get(MapId.Small), timeScale: 5000, stub, _logDirectory, logSizeCapBytesOverride: 512);
        session.Disconnect();

        for (var beat = 0; beat < 5000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        session.Dispose();

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        var truncatedRecords = lines.Where(l => l.Kind == "truncated").ToList();

        Assert.Single(truncatedRecords);

        // The match still played to a decided outcome despite logging having stopped.
        Assert.NotEqual(MatchOutcome.InProgress, session.Match.Outcome);
    }

    [Fact]
    public async Task ATruncatedLog_CutMidLine_IsReadableAsIncomplete_UpToTheLastWholeLine()
    {
        var matchId = "cut-mid-line";
        using var stub = new ClientWebSocket();
        using (var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 5000, stub, _logDirectory))
        {
            session.Disconnect();
            for (var beat = 0; beat < 200 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
            {
                await session.TickAsync(CancellationToken.None);
            }

            session.Dispose();
        }

        var path = LogPathFor(matchId);
        var wholeBytes = await File.ReadAllBytesAsync(path);
        Assert.True(wholeBytes.Length > 20, "Expected a non-trivial log to cut.");

        var cutBytes = wholeBytes[..(wholeBytes.Length - 10)]; // mid the trailer line, no trailing LF
        var cutPath = Path.Combine(_logDirectory, "cut.jsonl");
        await File.WriteAllBytesAsync(cutPath, cutBytes);

        var lines = MatchLogReader.ReadLines(cutPath);

        Assert.False(MatchLogReader.IsComplete(lines));
        Assert.Equal("header", lines[0].Kind);
        Assert.DoesNotContain(lines, l => l.Kind == "trailer");
    }

    [Theory]
    [InlineData(MapId.Small)]
    [InlineData(MapId.Medium)]
    [InlineData(MapId.Big)]
    public async Task ReplayEquivalence_TheLoggedCommandsReproduceTheTrailersFinalHash(MapId mapId)
    {
        var matchId = "replay-" + mapId;
        using var stub = new ClientWebSocket();
        using (var session = new MatchSession(matchId, MapCatalog.Get(mapId), timeScale: 2000, stub, _logDirectory))
        {
            // Disconnected immediately so both sides keep acting via a brain (D-87 exercises both
            // command sources, not just the client path). Bounded to a few hundred beats rather than
            // however long it takes to decide: Big carries two neutral towers guarding its forge and
            // - per MatchLifecycleTests's own note - "can legitimately run long without ever losing"
            // against a passive opponent; driving it for as long as that can take would itself blow
            // the per-match log size cap on decision volume alone, which is a separate concern from
            // what this test proves. Small and Medium reliably decide well inside this budget; Big
            // legitimately may not, and D-89's proof - the logged and replayed hashes agreeing - does
            // not require a decided outcome to be meaningful.
            session.Disconnect();

            for (var beat = 0; beat < 300 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
            {
                await session.TickAsync(CancellationToken.None);
            }

            session.Dispose();
        }

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        Assert.True(MatchLogReader.IsComplete(lines));

        var replay = MatchLogReplayReader.Replay(LogPathFor(matchId));

        // Every logged hash record must match the replay's own hash computed at that same tick -
        // localises a divergence to its five-second window rather than only reporting one at the
        // very end (D-89).
        Assert.NotEmpty(replay.HashChecks);
        Assert.All(replay.HashChecks, check => Assert.Equal(check.LoggedHash, check.ReplayHash));

        var trailer = lines.Single(l => l.Kind == "trailer");
        Assert.Equal(trailer.Root.GetProperty("finalHash").GetUInt64(), replay.FinalHash);
        Assert.Equal(trailer.Root.GetProperty("outcome").GetString(), replay.Outcome.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task ReplayEquivalence_CoversAMatchThatRanPastADisconnectWithTheSubstituteActive()
    {
        var matchId = "replay-disconnect";
        using var stub = new ClientWebSocket();
        using (var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 1, stub, _logDirectory))
        {
            session.Disconnect();
            for (var beat = 0; beat < 200_000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
            {
                await session.TickAsync(CancellationToken.None);
            }

            session.Dispose();
        }

        var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
        Assert.True(MatchLogReader.IsComplete(lines));
        Assert.Contains(
            lines,
            l => l.Kind == "command"
                && l.Root.GetProperty("source").GetString() == "brain"
                && l.Root.GetProperty("playerId").GetInt32() == 1
                && l.Root.GetProperty("tick").GetInt64() > ServerTuning.DisconnectGraceTicks);

        var replay = MatchLogReplayReader.Replay(LogPathFor(matchId));
        var trailer = lines.Single(l => l.Kind == "trailer");
        Assert.Equal(trailer.Root.GetProperty("finalHash").GetUInt64(), replay.FinalHash);
    }

    [Fact]
    public async Task TwoLogsOfTheSameFixedCommandSequence_AreByteIdentical_OnceMatchIdAndTimestampsAreElided()
    {
        var firstPath = await PlayFixedSequenceAsync("byte-identical-a");
        var secondPath = await PlayFixedSequenceAsync("byte-identical-b");

        var first = ElideVolatileFields(await File.ReadAllLinesAsync(firstPath));
        var second = ElideVolatileFields(await File.ReadAllLinesAsync(secondPath));

        Assert.Equal(first, second);
    }

    private async Task<string> PlayFixedSequenceAsync(string matchId)
    {
        using var stub = new ClientWebSocket();
        using var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 1, stub, _logDirectory);

        // A fixed sequence: one accepted upgrade, one rejected send, then enough idle beats to reach
        // the first hash interval - the same commands submitted at the same beat both times.
        session.Inbox.Enqueue((1, GatewayCommand.Upgrade(baseId: 0)));
        await session.TickAsync(CancellationToken.None);

        session.Inbox.Enqueue((2, GatewayCommand.SendArmy(from: 1, to: 0, SendStrength.Half)));
        await session.TickAsync(CancellationToken.None);

        for (var beat = 0; beat < ServerTuning.LogHashIntervalTicks + 10; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        session.Dispose();
        return LogPathFor(matchId);
    }

    /// <summary>Strips <c>matchId</c> and the header's/trailer's timestamp field (D-91) before a byte comparison.</summary>
    private static string[] ElideVolatileFields(string[] lines)
    {
        var result = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            using var document = JsonDocument.Parse(lines[i]);
            var root = document.RootElement;
            var kind = root.GetProperty("kind").GetString();

            if (kind is "header" or "trailer")
            {
                Assert.True(root.TryGetProperty("timestampUtc", out _), $"Expected a timestamp on the {kind} record.");
            }
            else
            {
                Assert.False(root.TryGetProperty("timestampUtc", out _), $"No wall-clock timestamp is allowed on a {kind} record (D-91).");
                Assert.False(root.TryGetProperty("matchId", out _), $"No matchId is allowed on a {kind} record.");
            }

            result[i] = RemoveFields(root, "matchId", "timestampUtc");
        }

        return result;
    }

    private static string RemoveFields(JsonElement root, params string[] fieldsToRemove)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (Array.IndexOf(fieldsToRemove, property.Name) >= 0)
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task NConcurrentSessions_EachProduceACompleteWellFormedLog_WithNoCrossContamination()
    {
        const int sessionCount = 6;
        var sessions = new List<MatchSession>();
        var matchIds = new List<string>();

        try
        {
            for (var i = 0; i < sessionCount; i++)
            {
                var matchId = "concurrent-" + i;
                matchIds.Add(matchId);
                using var stub = new ClientWebSocket();
                var session = new MatchSession(matchId, MapCatalog.Get(MapId.Small), timeScale: 2000, stub, _logDirectory);
                session.Disconnect();
                sessions.Add(session);
            }

            // TickScheduler walks every live session on one thread each beat - mirrored here so no
            // two sessions' writers are ever touched concurrently, matching production exactly.
            for (var beat = 0; beat < 5000 && sessions.Any(s => s.Match.Outcome == MatchOutcome.InProgress); beat++)
            {
                foreach (var session in sessions)
                {
                    if (session.Match.Outcome == MatchOutcome.InProgress)
                    {
                        await session.TickAsync(CancellationToken.None);
                    }
                }
            }

            foreach (var session in sessions)
            {
                session.Dispose();
            }

            foreach (var matchId in matchIds)
            {
                var lines = MatchLogReader.ReadLines(LogPathFor(matchId));
                Assert.True(MatchLogReader.IsComplete(lines));

                var header = lines[0];
                Assert.Equal(matchId, header.Root.GetProperty("matchId").GetString());

                var trailer = lines.Single(l => l.Kind == "trailer");
                Assert.Equal(matchId, header.Root.GetProperty("matchId").GetString());
                Assert.True(trailer.Root.GetProperty("tick").GetInt64() >= 0);
            }
        }
        finally
        {
            foreach (var session in sessions)
            {
                session.Dispose();
            }
        }
    }

    [Fact]
    public async Task TwoRealSessionsThroughTheServer_ProduceSeparateCompleteLogs()
    {
        await using var fixture = new ServerFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var a = await fixture.ConnectAsync();
            await a.HandshakeAsync();
            var createdA = await a.CreateSessionAsync("Small", timeScale: 2000);

            await using var b = await fixture.ConnectAsync();
            await b.HandshakeAsync();
            var createdB = await b.CreateSessionAsync("Small", timeScale: 2000);

            await a.WaitForOutcomeAsync(createdA.Snapshot!, TimeSpan.FromSeconds(60));
            await b.WaitForOutcomeAsync(createdB.Snapshot!, TimeSpan.FromSeconds(60));

            await a.DisposeAsync();
            await b.DisposeAsync();

            // Give the scheduler a moment to notice the disconnect and evict once each match is
            // decided and abandoned.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            var pathA = Path.Combine(fixture.LogDirectory, createdA.MatchId + ".jsonl");
            var pathB = Path.Combine(fixture.LogDirectory, createdB.MatchId + ".jsonl");
            while (DateTime.UtcNow < deadline && (!IsCompleteLog(pathA) || !IsCompleteLog(pathB)))
            {
                await Task.Delay(100);
            }

            Assert.True(IsCompleteLog(pathA));
            Assert.True(IsCompleteLog(pathB));

            var headerA = MatchLogReader.ReadLines(pathA)[0];
            var headerB = MatchLogReader.ReadLines(pathB)[0];
            Assert.Equal(createdA.MatchId, headerA.Root.GetProperty("matchId").GetString());
            Assert.Equal(createdB.MatchId, headerB.Root.GetProperty("matchId").GetString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static bool IsCompleteLog(string path) => File.Exists(path) && MatchLogReader.IsComplete(MatchLogReader.ReadLines(path));
}
