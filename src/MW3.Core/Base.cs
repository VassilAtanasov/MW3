namespace MW3.Core;

/// <summary>
/// A base on the map. Neutral is the absence of an owner, never a sentinel player id (D-11).
/// Constructed only by <see cref="Match"/>; garrison count, owner, level, type and production
/// progress change only through <see cref="Match.Advance"/> and
/// <see cref="Match.Execute(SendArmyCommand)"/> / <see cref="Match.Execute(UpgradeCommand)"/> /
/// <see cref="Match.Execute(ConvertCommand)"/> (D-13).
/// </summary>
public sealed class Base
{
    internal Base(int id, MapPoint position, int garrisonCount, Player? owner)
    {
        Id = id;
        Position = position;
        GarrisonCount = garrisonCount;
        Owner = owner;
        Level = LevelTable.MinLevel;
        Type = BaseType.Producer;
    }

    public int Id { get; }

    public MapPoint Position { get; }

    public int GarrisonCount { get; internal set; }

    public Player? Owner { get; internal set; }

    /// <summary>
    /// This base's level, between <see cref="LevelTable.MinLevel"/> and
    /// <see cref="LevelTable.MaxLevel"/>. Raised one step at a time by an
    /// <see cref="UpgradeCommand"/>, and dropped one step (never below the minimum) when the base
    /// is captured - the structure survives the fighting, but one level of the previous owner's
    /// investment is burned with it (D-23).
    /// </summary>
    public int Level { get; internal set; }

    /// <summary>
    /// Whether this base is a <see cref="BaseType.Producer"/> or a <see cref="BaseType.Tower"/>.
    /// Every base starts a producer (including neutral ones); changes only through a
    /// <see cref="ConvertCommand"/> and never through <see cref="Match.Advance"/>. A capture keeps
    /// the type while dropping one level - only <see cref="Level"/> is demoted (D-23).
    /// </summary>
    public BaseType Type { get; internal set; }

    /// <summary>
    /// Ticks accumulated toward this base's next unit. Exposed because it is real simulation state
    /// that determinism covers (D-12) - two matches advanced to the same tick must agree on it, not
    /// merely on garrison counts.
    /// </summary>
    public long ProductionProgressTicks { get; internal set; }

    /// <summary>
    /// The garrison this base produces up to at its current level. A production ceiling, not a
    /// storage limit: armies arriving from elsewhere stack above it and nothing is destroyed
    /// (D-21).
    /// </summary>
    public int GarrisonCap => LevelTable.GarrisonCap(Level);
}
