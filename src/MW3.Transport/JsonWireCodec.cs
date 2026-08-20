using System.Text.Json;

namespace MW3.Transport;

/// <summary>The shipped <see cref="IWireCodec"/> implementation (D-64): JSON over <see cref="WireJsonContext"/>.</summary>
public sealed class JsonWireCodec : IWireCodec
{
    /// <inheritdoc />
    public byte[] Encode(WireMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, WireJsonContext.Default.WireMessage);
    }

    /// <inheritdoc />
    public WireMessage Decode(ReadOnlyMemory<byte> bytes)
    {
        return JsonSerializer.Deserialize(bytes.Span, WireJsonContext.Default.WireMessage)
            ?? throw new JsonException("A wire message must not deserialize to null.");
    }
}
