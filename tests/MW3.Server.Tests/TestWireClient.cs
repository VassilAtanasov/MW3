using System.Net.WebSockets;
using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// A raw wire client for the test suite: talks <see cref="WireMessage"/> directly over a
/// <see cref="WebSocket"/> from <c>TestServer.CreateWebSocketClient()</c>, without going through
/// <see cref="RemoteMatchGateway"/> - so a test can drive the handshake itself and assert on
/// malformed or out-of-protocol input the production client would never construct.
/// </summary>
internal sealed class TestWireClient : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly JsonWireCodec _codec = new();

    internal TestWireClient(WebSocket socket) => _socket = socket;

    internal WebSocketState State => _socket.State;

    internal Task SendAsync(WireMessage message, CancellationToken cancellationToken = default) =>
        WebSocketFraming.SendAsync(_socket, _codec.Encode(message), cancellationToken);

    /// <summary>Sends raw bytes rather than an encoded <see cref="WireMessage"/> - for malformed-payload tests.</summary>
    internal Task SendRawAsync(byte[] bytes, CancellationToken cancellationToken = default) =>
        WebSocketFraming.SendAsync(_socket, bytes, cancellationToken);

    internal async Task<WireMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await WebSocketFraming.ReceiveAsync(_socket, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : _codec.Decode(bytes);
    }

    /// <summary>Sends Hello and returns the server's Welcome.</summary>
    internal async Task<WireMessage> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(WireMessage.Hello(MatchSnapshot.CurrentProtocolVersion), cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The server closed the connection instead of sending Welcome.");
    }

    /// <summary>Sends CreateSession and returns the server's SessionCreated.</summary>
    internal async Task<WireMessage> CreateSessionAsync(string mapName, long timeScale, CancellationToken cancellationToken = default)
    {
        await SendAsync(WireMessage.CreateSession(MatchSnapshot.CurrentProtocolVersion, mapName, timeScale), cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The server closed the connection instead of sending SessionCreated.");
    }

    /// <summary>
    /// Applies every <see cref="WireMessageKind.Events"/> message that arrives before
    /// <paramref name="timeout"/> passes, asserting the server's snapshot hash (D-71) against the
    /// client-reconstructed one at every single one - structural equality "at every send", not just
    /// at the end. Stops early if the match reaches a decided outcome; otherwise returns whatever
    /// was last reconstructed when the timeout hits, without throwing - some matches (a well-defended
    /// map against a passive opponent) can legitimately run long, and that is not this method's
    /// concern to adjudicate.
    /// </summary>
    internal async Task<MatchSnapshot> WaitForOutcomeAsync(MatchSnapshot current, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var snapshot = current;
        try
        {
            while (snapshot.Outcome == MatchOutcome.InProgress)
            {
                var message = await ReceiveAsync(cts.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The server closed the connection before the match concluded.");

                if (message.Kind != WireMessageKind.Events)
                {
                    continue;
                }

                snapshot = SnapshotApplier.Apply(message.Events!, snapshot);
                Assert.Equal(SnapshotHash.Compute(snapshot), message.SnapshotHash!.Value);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The per-call timeout elapsed, not the caller's own token - return the last known-good,
            // hash-verified snapshot rather than fail the wait itself.
        }

        return snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Best-effort.
            }
        }

        _socket.Dispose();
    }
}
