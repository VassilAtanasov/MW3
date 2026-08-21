using System.Text.Json.Serialization;

namespace MW3.Transport;

/// <summary>
/// The source-generated serializer for everything that crosses the wire (D-72, D-77) - no
/// reflection at run time, so the shape survives trimming and AOT on the Android head. This is the
/// codec's shipped home: FR-1 parked an equivalent context in <c>tests/MW3.Core.Tests</c> "until
/// FR-4 gives it one", and that test copy is deleted, not kept alongside this one.
/// </summary>
// Enums travel as NAMES, not ordinals - see the deleted test copy's own note, preserved here: a
// BaseType reorder must not silently reinterpret a stored Tower as a Forge while the protocol
// version stays put.
[JsonSourceGenerationOptions(
    Converters = new[] { typeof(MapObstacleJsonConverter) },
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WireMessage))]
[JsonSerializable(typeof(MatchSnapshot))]
[JsonSerializable(typeof(EventBatch))]
[JsonSerializable(typeof(GatewayCommand))]
[JsonSerializable(typeof(GatewayCommandResult))]
[JsonSerializable(typeof(BaseSnapshot))]
public sealed partial class WireJsonContext : JsonSerializerContext
{
}
