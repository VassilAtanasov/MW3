using System.Text.Json;
using System.Text.Json.Serialization;

namespace MW3.Transport;

/// <summary>
/// Reads and writes a <see cref="MapObstacle"/> through its validating constructor (D-72). Moved
/// here unchanged from <c>tests/MW3.Core.Tests/MapObstacleJsonConverter.cs</c>, which FR-1 used as a
/// temporary home until this feature gave the codec a permanent one.
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
