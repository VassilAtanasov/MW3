using System.Net.WebSockets;
using MW3.Core;
using MW3.Transport;

namespace MW3.Server;

/// <summary>
/// The whole lifecycle of one WebSocket connection: the <see cref="WireMessageKind.Hello"/>/
/// <see cref="WireMessageKind.Welcome"/> handshake, <see cref="WireMessageKind.CreateSession"/>,
/// then a receive loop that validates every inbound <see cref="WireMessageKind.Command"/> and
/// enqueues it on the session's inbox (§"Every inbound message is validated where it is
/// deserialized" - a malformed or out-of-range command never reaches <c>Match</c>). One connection
/// is one match (§"The connection is per match").
/// </summary>
internal static class ConnectionHandler
{
    private static readonly string[] _mapNames = BuildMapNames();

    internal static async Task HandleAsync(WebSocket socket, MatchSessionRegistry registry, string logDirectory, CancellationToken cancellationToken)
    {
        var codec = new JsonWireCodec();
        MatchSession? session = null;

        try
        {
            if (!await TryHandshakeAsync(socket, codec, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            session = await CreateSessionAsync(socket, codec, registry, logDirectory, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return;
            }

            await ReceiveLoopAsync(socket, codec, session, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The peer went away mid-message - the disconnect is handled uniformly below.
        }
        catch (OperationCanceledException)
        {
            // Server shutting down.
        }
        catch (InvalidOperationException)
        {
            // A single message exceeded WebSocketFraming's size guard - the connection is a lost
            // cause; the session it belonged to (if any) is still cleaned up below.
        }
        finally
        {
            session?.Disconnect();
        }
    }

    private static async Task<bool> TryHandshakeAsync(WebSocket socket, JsonWireCodec codec, CancellationToken cancellationToken)
    {
        var bytes = await WebSocketFraming.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return false;
        }

        WireMessage hello;
        try
        {
            hello = codec.Decode(bytes);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"malformed Hello: {ex.Message}"), cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (hello.Kind != WireMessageKind.Hello)
        {
            await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"expected Hello, got {hello.Kind}"), cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (hello.ProtocolVersion != MatchSnapshot.CurrentProtocolVersion)
        {
            await CloseWithErrorAsync(
                socket,
                codec,
                FormattableString.Invariant($"protocol version mismatch: client is {hello.ProtocolVersion}, server is {MatchSnapshot.CurrentProtocolVersion}"),
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        var welcome = WireMessage.Welcome(MatchSnapshot.CurrentProtocolVersion, _mapNames);
        await WebSocketFraming.SendAsync(socket, codec.Encode(welcome), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<MatchSession?> CreateSessionAsync(
        WebSocket socket, JsonWireCodec codec, MatchSessionRegistry registry, string logDirectory, CancellationToken cancellationToken)
    {
        var bytes = await WebSocketFraming.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        WireMessage create;
        try
        {
            create = codec.Decode(bytes);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
        {
            await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"malformed CreateSession: {ex.Message}"), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (create.Kind != WireMessageKind.CreateSession)
        {
            await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"expected CreateSession, got {create.Kind}"), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (create.ProtocolVersion != MatchSnapshot.CurrentProtocolVersion)
        {
            await CloseWithErrorAsync(
                socket,
                codec,
                FormattableString.Invariant($"protocol version mismatch: client is {create.ProtocolVersion}, server is {MatchSnapshot.CurrentProtocolVersion}"),
                cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (create.TimeScale is null || create.TimeScale <= 0)
        {
            await CloseWithErrorAsync(socket, codec, "time scale must be a positive integer", cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (!TryResolveMapName(create.MapName!, out var mapId))
        {
            await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"unknown map '{create.MapName}'"), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (registry.Count >= ServerTuning.MaxConcurrentSessions)
        {
            await CloseWithErrorAsync(socket, codec, "the server is at its concurrent session limit", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var matchId = Guid.NewGuid().ToString("n");
        var session = new MatchSession(matchId, MapCatalog.Get(mapId), create.TimeScale.Value, socket, logDirectory);
        if (!registry.TryAdd(session))
        {
            await CloseWithErrorAsync(socket, codec, "could not register the session", cancellationToken).ConfigureAwait(false);
            return null;
        }

        var sessionCreated = WireMessage.SessionCreated(MatchSnapshot.CurrentProtocolVersion, matchId, session.LastSentSnapshot);
        await WebSocketFraming.SendAsync(socket, codec.Encode(sessionCreated), cancellationToken).ConfigureAwait(false);
        return session;
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, JsonWireCodec codec, MatchSession session, CancellationToken cancellationToken)
    {
        while (true)
        {
            var bytes = await WebSocketFraming.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return;
            }

            WireMessage message;
            try
            {
                message = codec.Decode(bytes);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
            {
                await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"malformed Command: {ex.Message}"), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message.Kind != WireMessageKind.Command)
            {
                await CloseWithErrorAsync(socket, codec, FormattableString.Invariant($"expected Command, got {message.Kind}"), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message.ProtocolVersion != MatchSnapshot.CurrentProtocolVersion)
            {
                await CloseWithErrorAsync(
                    socket,
                    codec,
                    FormattableString.Invariant($"protocol version mismatch: client is {message.ProtocolVersion}, server is {MatchSnapshot.CurrentProtocolVersion}"),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var command = message.Command!;
            if (!session.BaseExists(command.FromBaseId) || (command.ToBaseId is not null && !session.BaseExists(command.ToBaseId.Value)))
            {
                await CloseWithErrorAsync(socket, codec, "command names a base id that does not exist in this match", cancellationToken).ConfigureAwait(false);
                return;
            }

            session.Inbox.Enqueue((message.CommandId!.Value, command));
        }
    }

    private static async Task CloseWithErrorAsync(WebSocket socket, JsonWireCodec codec, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var error = WireMessage.ErrorFor(MatchSnapshot.CurrentProtocolVersion, reason);
            await WebSocketFraming.SendAsync(socket, codec.Encode(error), cancellationToken).ConfigureAwait(false);
            await socket.CloseAsync(WebSocketCloseStatus.ProtocolError, reason, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The peer is already gone; nothing left to tell it.
        }
    }

    private static bool TryResolveMapName(string mapName, out MapId mapId)
    {
        foreach (var id in MapCatalog.AllIds)
        {
            if (string.Equals(id.ToString(), mapName, StringComparison.OrdinalIgnoreCase))
            {
                mapId = id;
                return true;
            }
        }

        mapId = default;
        return false;
    }

    private static string[] BuildMapNames()
    {
        var ids = MapCatalog.AllIds;
        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = ids[i].ToString();
        }

        return names;
    }
}
