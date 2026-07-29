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
    /// This base's construction in progress, or null if it is not building anything (D-30, FR-3c).
    /// Set by an accepted <see cref="UpgradeCommand"/> or <see cref="ConvertCommand"/>, cleared by
    /// <see cref="Match.Advance"/> on completion or discarded outright on capture - never cancelled
    /// any other way, since the rules offer no cancel.
    /// </summary>
    public PendingConstruction? Construction { get; internal set; }

    /// <summary>
    /// The tick this base last changed owner, or null if it never has. Compared against the current
    /// tick to decide the recapture grace (D-30, FR-3c) - a remembered tick rather than a countdown,
    /// so it needs no per-tick stepping and survives irregular chunking (D-12).
    /// </summary>
    public long? LastOwnerChangeTick { get; internal set; }

    /// <summary>
    /// The owner this base had immediately before <see cref="LastOwnerChangeTick"/>, or null if that
    /// owner was neutral (or if the base has never changed owner). A capture within
    /// <see cref="LevelTable.RecaptureGraceTicks"/> skips the usual demotion only when the capturing
    /// player equals this value - a true retake, not merely any capture within the window.
    /// </summary>
    public Player? OwnerBeforeLastChange { get; internal set; }

    /// <summary>
    /// This base's level, between <see cref="LevelTable.MinLevel"/> and this base's own
    /// <see cref="MaxLevel"/>. Raised one step at a time by an <see cref="UpgradeCommand"/>, and
    /// dropped one step (never below the minimum) when the base is captured - the structure
    /// survives the fighting, but one level of the previous owner's investment is burned with it
    /// (D-23).
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
    /// The garrison this base produces up to at its current level, or null if its type has no cap
    /// (a tower). A production ceiling, not a storage limit: armies arriving from elsewhere stack
    /// above it and nothing is destroyed (D-21).
    /// </summary>
    public int? GarrisonCap => LevelTable.GarrisonCap(Type, Level);

    /// <summary>
    /// The highest level this base's type's ladder defines. Not necessarily reachable by upgrading -
    /// see <see cref="MaxUpgradableLevel"/> for the level <see cref="Match.Execute(UpgradeCommand)"/>
    /// actually stops at.
    /// </summary>
    public int MaxLevel => LevelTable.MaxLevel(Type);

    /// <summary>
    /// The highest level this base can reach by upgrading. <see cref="Match.Execute(UpgradeCommand)"/>
    /// and <see cref="Match.AvailableActions"/> gate on this, not on <see cref="MaxLevel"/>: a village
    /// stops upgrading at level 4 even though its ladder also defines level 5.
    /// </summary>
    public int MaxUpgradableLevel => LevelTable.MaxUpgradableLevel(Type);

    /// <summary>
    /// Units it costs to raise this base to the next level. Only valid below
    /// <see cref="MaxUpgradableLevel"/>.
    /// </summary>
    public int UpgradeCost => LevelTable.UpgradeCost(Type, Level);

    /// <summary>How thick this base's level ring is drawn, as a fraction of its radius.</summary>
    public double RingThicknessFractionOfRadius => LevelTable.RingThicknessFractionOfRadius(Type, Level);

    /// <summary>
    /// The percentage of the flat 1:1 baseline this base defends at right now (D-29), read from its
    /// own type and level. A level-1 tower (140%) already matches a level-5 village (140%).
    /// </summary>
    public int DefencePercentage => LevelTable.DefencePercentage(Type, Level);
}
