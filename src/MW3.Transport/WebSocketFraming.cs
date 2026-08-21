using System.Net.WebSockets;

namespace MW3.Transport;

/// <summary>
/// One WebSocket message == one <see cref="WireMessage"/> (D-64). Shared by the client
/// (<see cref="ClientWebSocket"/>) and the server (ASP.NET Core's accepted <see cref="WebSocket"/>) -
/// both derive from the same abstract base, so one implementation frames both directions.
/// </summary>
public static class WebSocketFraming
{
    private const int _receiveBufferBytes = 8192;

    // A snapshot for the largest shipped map is nowhere near this; it exists so a peer that never
    // sets EndOfMessage (malformed client, or a buggy retry loop) cannot grow this process's memory
    // unboundedly one frame at a time.
    private const int _maxMessageBytes = 4 * 1024 * 1024;

    /// <summary>Sends <paramref name="payload"/> as one complete text message.</summary>
    public static async Task SendAsync(WebSocket socket, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one complete message, reassembling it from however many frames it arrived in. Returns
    /// null if the peer closed the connection instead of sending a message.
    /// </summary>
    public static async Task<byte[]?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        using var buffered = new MemoryStream();
        var buffer = new byte[_receiveBufferBytes];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            await buffered.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            if (buffered.Length > _maxMessageBytes)
            {
                throw new InvalidOperationException(
                    FormattableString.Invariant($"A single WebSocket message exceeded {_maxMessageBytes} bytes without completing."));
            }

            if (result.EndOfMessage)
            {
                return buffered.ToArray();
            }
        }
    }
}
