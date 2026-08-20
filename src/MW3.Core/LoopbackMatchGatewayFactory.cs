namespace MW3.Core;

/// <summary>
/// The composition root's gateway factory (D-74): every map <see cref="MapCatalog"/> holds, named,
/// in catalogue order, and a fresh <see cref="LoopbackMatchGateway"/> for whichever one is asked
/// for. A head constructs one of these and injects it into the client, which is how the client
/// offers three maps without ever naming one.
///
/// The names are <see cref="MapId"/>'s own member names, which is exactly what
/// <c>MatchSnapshot.MapId</c> already carries (D-73) - so a client comparing the map it asked for
/// against the map a snapshot says it got is comparing like with like.
/// </summary>
public sealed class LoopbackMatchGatewayFactory : IMatchGatewayFactory
{
    private static readonly string[] _mapNames = BuildMapNames();

    /// <inheritdoc />
    public IReadOnlyList<string> MapNames => _mapNames;

    /// <inheritdoc />
    public IMatchGateway CreateForMap(string mapName) => new LoopbackMatchGateway(MapCatalog.Get(Resolve(mapName)));

    /// <summary>
    /// The <see cref="MapId"/> called <paramref name="mapName"/>, matched without regard to case -
    /// the same latitude <c>--map small</c> has always had, kept here so the flag's parsing has one
    /// authority rather than a copy per head.
    /// </summary>
    private static MapId Resolve(string mapName)
    {
        if (mapName is null)
        {
            throw new ArgumentNullException(nameof(mapName));
        }

        foreach (var id in MapCatalog.AllIds)
        {
            if (string.Equals(id.ToString(), mapName, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        throw new ArgumentException(
            FormattableString.Invariant($"There is no map called '{mapName}'. Known maps: {string.Join(", ", _mapNames)}."),
            nameof(mapName));
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
