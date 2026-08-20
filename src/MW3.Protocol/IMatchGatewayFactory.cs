namespace MW3.Protocol;

/// <summary>
/// Creates a gateway for a named map, and says which names there are (D-74). The client hardcodes
/// no map identity: it renders one button per name this reports, in this order, and asks for a match
/// by that same name - which is the name <see cref="MatchSnapshot.MapId"/> already carries under
/// D-73, so a client holds one map identity concept rather than two. A future project can add a map
/// without the renderer learning about it.
/// </summary>
public interface IMatchGatewayFactory
{
    /// <summary>Every map that can be played, in the catalogue's own order.</summary>
    IReadOnlyList<string> MapNames { get; }

    /// <summary>
    /// A fresh gateway for the map called <paramref name="mapName"/> - a new match against a new
    /// opponent every time. Throws for a name not in <see cref="MapNames"/>.
    /// </summary>
    IMatchGateway CreateForMap(string mapName);
}
