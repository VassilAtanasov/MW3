using System.Text.Json;
using System.Text.Json.Serialization;
using MW3.Protocol;

namespace MW3.Core.Tests;

/// <summary>
/// Reads and writes a <see cref="MapObstacle"/> through its validating constructor.
///
/// It needs one where the other snapshot types do not, and the reason is worth writing down because
/// it will come up again at FR-4. <see cref="MapObstacle"/> is a struct whose four extents are
/// get-only and whose constructor rejects an inverted or out-of-bounds rectangle (phase 7 D-50).
/// <c>System.Text.Json</c> reaches for a struct's always-present parameterless constructor and then
/// assigns settable properties - so without this converter an obstacle deserializes as four zeroes,
/// silently, with the invariant intact and the value gone. The alternatives were to loosen the
/// properties to <c>init</c> (which would let any caller, not just the deserializer, build an
/// invalid obstacle) or to annotate the constructor with <c>[JsonConstructor]</c> (which
/// <c>MW3.Protocol</c> cannot do: the attribute lives in a NuGet package on <c>netstandard2.1</c>,
/// and that project takes no packages). Teaching the codec how to rebuild the type is the option
/// that costs the type nothing.
/// </summary>
internal sealed class MapObstacleJsonConverter : JsonConverter<MapObstacle>
{
    public override MapObstacle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("An obstacle must be a JSON object.");
        }

        double? minX = null;
        double? minY = null;
        double? maxX = null;
        double? maxY = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a property name while reading an obstacle.");
            }

            var name = reader.GetString();
            reader.Read();
            var value = reader.GetDouble();

            switch (name)
            {
                case nameof(MapObstacle.MinX):
                    minX = value;
                    break;
                case nameof(MapObstacle.MinY):
                    minY = value;
                    break;
                case nameof(MapObstacle.MaxX):
                    maxX = value;
                    break;
                case nameof(MapObstacle.MaxY):
                    maxY = value;
                    break;
                default:
                    throw new JsonException(
                        FormattableString.Invariant($"An obstacle has no '{name}' field."));
            }
        }

        if (minX is null || minY is null || maxX is null || maxY is null)
        {
            throw new JsonException("An obstacle must carry all four of its extents.");
        }

        // Through the constructor, so a payload describing an impossible rectangle is rejected at
        // the boundary rather than cast into shape.
        return new MapObstacle(minX.Value, minY.Value, maxX.Value, maxY.Value);
    }

    public override void Write(Utf8JsonWriter writer, MapObstacle value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteNumber(nameof(MapObstacle.MinX), value.MinX);
        writer.WriteNumber(nameof(MapObstacle.MinY), value.MinY);
        writer.WriteNumber(nameof(MapObstacle.MaxX), value.MaxX);
        writer.WriteNumber(nameof(MapObstacle.MaxY), value.MaxY);
        writer.WriteEndObject();
    }
}
