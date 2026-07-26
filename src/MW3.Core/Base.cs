namespace MW3.Core;

/// <summary>
/// A base on the map. Neutral is the absence of an owner, never a sentinel player id (D-11).
/// Constructed only by <see cref="Match"/>; garrison count changes only through
/// <see cref="Match.Advance"/> (D-13).
/// </summary>
public sealed class Base
{
    internal Base(int id, MapPoint position, int garrisonCount, Player? owner)
    {
        Id = id;
        Position = position;
        GarrisonCount = garrisonCount;
        Owner = owner;
    }

    public int Id { get; }

    public MapPoint Position { get; }

    public int GarrisonCount { get; internal set; }

    public Player? Owner { get; }
}
