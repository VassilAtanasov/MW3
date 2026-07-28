namespace MW3.Core;

/// <summary>
/// The level ladder (D-22): the single source for every per-level tuning number, so the simulation,
/// the AI, and the tests read one table rather than each hardcoding a value. A short fixed ladder
/// rather than a formula, because every interesting ladder is non-linear and a formula invites
/// tuning by exponent; a table in code rather than a content file, because this phase keeps one map
/// and one ruleset hardcoded (REQUIREMENTS.md §6).
/// <para>
/// A level buys economy only - capacity and production rate - never combat strength: combat stays
/// the plain 1:1 arithmetic phase 2 established (D-15, D-22).
/// </para>
/// </summary>
public static class LevelTable
{
    /// <summary>The level every base starts at, and the floor a capture demotion cannot go below.</summary>
    public const int MinLevel = 1;

    public const int MaxLevel = 3;

    private static readonly int[] _garrisonCaps = { 20, 35, 50 };

    private static readonly long[] _productionPeriodTicks = { 10, 7, 5 };

    // Indexed by the level being upgraded *from*, so [0] is the cost to reach level 2. The first
    // upgrade is deliberately affordable from the starting garrison of 10 without waiting, so
    // "grow first" is a live opening move rather than something a player only saves toward.
    private static readonly int[] _upgradeCosts = { 6, 16 };

    // A dimensionless fraction of a base's drawn radius, not a pixel count - MW3.Core has no notion
    // of pixels (D-2). MW3.Game multiplies this by whatever radius the viewport produced, so the
    // ring stays a fixed proportion of the base at any resolution (D-14) with no per-level literal
    // duplicated at the call site (D-22).
    private static readonly double[] _ringThicknessFractionOfRadius = { 0.06, 0.14, 0.24 };

    /// <summary>
    /// The garrison a base of this level produces up to. It is a production ceiling, not a storage
    /// limit (D-21): arriving armies stack above it freely and nothing is ever destroyed for
    /// exceeding it - the base simply stops producing until it is back under.
    /// </summary>
    public static int GarrisonCap(int level) => _garrisonCaps[IndexOfLevel(level)];

    /// <summary>Ticks a base of this level takes to produce one unit while below its cap.</summary>
    public static long ProductionPeriodTicks(int level) => _productionPeriodTicks[IndexOfLevel(level)];

    /// <summary>
    /// Units it costs to raise a base from <paramref name="fromLevel"/> to the next level. Only
    /// defined below <see cref="MaxLevel"/> - a caller must reject an already-maxed base rather
    /// than ask what the impossible upgrade would cost.
    /// </summary>
    public static int UpgradeCost(int fromLevel)
    {
        if (fromLevel < MinLevel || fromLevel >= MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fromLevel),
                fromLevel,
                FormattableString.Invariant($"Upgrade cost is defined only for levels {MinLevel} to {MaxLevel - 1}."));
        }

        return _upgradeCosts[fromLevel - MinLevel];
    }

    /// <summary>
    /// How thick a base's level ring is drawn, as a fraction of its radius - the only place a
    /// level's visible ring thickness is defined, so <c>MatchScreen</c> reads it rather than
    /// hardcoding one number per level.
    /// </summary>
    public static double RingThicknessFractionOfRadius(int level) => _ringThicknessFractionOfRadius[IndexOfLevel(level)];

    private static int IndexOfLevel(int level)
    {
        if (level < MinLevel || level > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                FormattableString.Invariant($"Level must be between {MinLevel} and {MaxLevel}."));
        }

        return level - MinLevel;
    }
}
