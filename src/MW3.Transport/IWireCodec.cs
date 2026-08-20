namespace MW3.Transport;

/// <summary>
/// The codec seam (D-64): encodes a <see cref="WireMessage"/> to bytes and back. JSON is the only
/// implementation this phase ships - binary is deliberately not built - but nothing on either side
/// of the wire depends on the encoding beyond this interface.
/// </summary>
public interface IWireCodec
{
    /// <summary>Encodes <paramref name="message"/> to bytes for one WebSocket frame.</summary>
    byte[] Encode(WireMessage message);

    /// <summary>
    /// Decodes one frame's bytes back to a <see cref="WireMessage"/>. Throws
    /// <see cref="System.Text.Json.JsonException"/> or <see cref="ArgumentException"/> on a
    /// malformed payload - never returns a partially-populated message. Callers on both sides treat
    /// either exception as "close the connection, name the reason" (§5).
    /// </summary>
    WireMessage Decode(ReadOnlyMemory<byte> bytes);
}
