using System.Net.WebSockets;

namespace MW3.Transport;

/// <summary>
/// The composition root's remote gateway factory (D-74, D-77). Constructing one performs the
/// startup pre-flight handshake: it connects to the server, exchanges
/// <see cref="WireMessageKind.Hello"/>/<see cref="WireMessageKind.Welcome"/>, and closes that
/// connection again. This is what lets a head validate <c>--server</c> and learn the map catalogue
/// before any graphics device is created (mirroring <c>--map</c> and <c>--time-scale</c>) - and it is
/// where the client learns the map names it offers, so it still hardcodes no map identity (D-74).
///
/// The connection this pre-flight opens is not the one a match plays on: each <see cref="CreateForMap"/>
/// opens its own, matching <see cref="RemoteMatchGateway"/>'s one-socket-per-match lifecycle.
/// </summary>
public sealed class RemoteMatchGatewayFactory : IMatchGatewayFactory
{
    private readonly Uri _serverUri;
    private readonly long _timeScale;
    private readonly JsonWireCodec _codec;

    /// <summary>
    /// Connects to <paramref name="serverUri"/> and validates it. Throws
    /// <see cref="WebSocketException"/> if the server is unreachable,
    /// <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> fires first
    /// (FR-5, D-81/D-82's bounded probe), or <see cref="InvalidOperationException"/> if it answers
    /// with something other than a matching <see cref="WireMessageKind.Welcome"/> - all are the
    /// caller's cue to treat the address as not reachable, before constructing anything that needs a
    /// graphics device.
    /// </summary>
    public RemoteMatchGatewayFactory(Uri serverUri, long timeScale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        if (timeScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be positive.");
        }

        _serverUri = serverUri;
        _timeScale = timeScale;
        _codec = new JsonWireCodec();
        MapNames = FetchMapNames(cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> MapNames { get; }

    /// <inheritdoc />
    public IMatchGateway CreateForMap(string mapName) => new RemoteMatchGateway(_serverUri, mapName, _timeScale, _codec);

    private IReadOnlyList<string> FetchMapNames(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.ConnectAsync(_serverUri, cancellationToken).GetAwaiter().GetResult();

        try
        {
            var helloBytes = _codec.Encode(WireMessage.Hello(MatchSnapshot.CurrentProtocolVersion));
            WebSocketFraming.SendAsync(socket, helloBytes, cancellationToken).GetAwaiter().GetResult();

            var replyBytes = WebSocketFraming.ReceiveAsync(socket, cancellationToken).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("The server closed the connection instead of sending Welcome.");

            var reply = _codec.Decode(replyBytes);
            if (reply.Kind == WireMessageKind.Error)
            {
                throw new InvalidOperationException(FormattableString.Invariant($"The server refused the connection: {reply.Reason}"));
            }

            if (reply.Kind != WireMessageKind.Welcome)
            {
                throw new InvalidOperationException(FormattableString.Invariant($"Expected Welcome from the server but got {reply.Kind}."));
            }

            if (reply.ProtocolVersion != MatchSnapshot.CurrentProtocolVersion)
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"Protocol version mismatch: client is {MatchSnapshot.CurrentProtocolVersion}, server is {reply.ProtocolVersion}."));
            }

            return reply.MapNames!;
        }
        finally
        {
            try
            {
                socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "preflight complete", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (WebSocketException)
            {
                // Best-effort: the socket is being discarded either way.
            }
            catch (OperationCanceledException)
            {
                // The probe was cancelled (timed out) - nothing to gracefully close.
            }
        }
    }
}
