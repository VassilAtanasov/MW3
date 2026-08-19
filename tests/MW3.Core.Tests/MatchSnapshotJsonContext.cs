using System.Text.Json.Serialization;
using MW3.Protocol;

namespace MW3.Core.Tests;

/// <summary>
/// The source-generated serializer for <see cref="MatchSnapshot"/> - no reflection at run time, so
/// the shape survives trimming and AOT, which is what the Android head will need when FR-4 puts a
/// snapshot on a wire.
///
/// It lives here rather than in <c>MW3.Protocol</c> for one reason, and it is worth stating because
/// it looks like the wrong home: <c>System.Text.Json</c> is in-box on <c>net10.0</c> but a NuGet
/// package on <c>netstandard2.1</c>, and <c>MW3.Protocol</c> is <c>netstandard2.1</c> (so
/// <c>MW3.Core</c> can reference it, S-2/D-2) with a hard no-<c>PackageReference</c> rule that
/// exists to make D-57's boundary provable. Nothing ships a serialized snapshot until FR-4, which
/// owns the codec seam (D-64) and is the feature that gives this context a permanent home in
/// whichever project targets <c>net10.0</c>. Until then it lives with the test that proves the
/// contract round-trips.
/// </summary>
[JsonSourceGenerationOptions(Converters = new[] { typeof(MapObstacleJsonConverter) })]
[JsonSerializable(typeof(MatchSnapshot))]
internal sealed partial class MatchSnapshotJsonContext : JsonSerializerContext
{
}
