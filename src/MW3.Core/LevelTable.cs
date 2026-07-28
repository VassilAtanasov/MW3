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
/// <para>
/// Villages and towers are separate ladders (D-28), sourced from MW2's published economy
/// (<c>docs/reference/MW2-RULES.md</c> §2.2, §2.3): they differ in level count, in upgrade price,
/// and in what a level buys - a village grows capacity and production, a tower has neither. A
/// <see cref="Base"/> reads whichever ladder matches its own <see cref="BaseType"/> through the
/// type-taking overloads below; no caller selects a table by hand.
/// </para>
/// </summary>
public static class LevelTable
{
    /// <summary>The level every base starts at, and the floor a capture demotion cannot go below.</summary>
    public const int MinLevel = 1;

    /// <summary>
    /// Units a <see cref="ConvertCommand"/> costs, identical in both directions (producer to tower
    /// and back). Conversion also resets the base to <see cref="MinLevel"/>, which is the load-bearing
    /// half of the cost (<c>MW2-RULES.md</c> §2.1).
    /// </summary>
    public const int ConversionCost = 30;

    /// <summary>
    /// The village ladder: five levels, garrison caps <c>20 × level</c>, production periods giving
    /// <c>0.33 × level</c> units/sec at a 50 ms tick, upgrade costs 5/10/20 to reach levels 2/3/4
    /// (<c>MW2-RULES.md</c> §2.2). Level 5 is defined - its cap and period exist - but is not
    /// reachable by upgrading: MW2 publishes no price for it and how it is reached at all is marked
    /// <c>[?]</c> in the reference, so it is modelled and left unreachable rather than given an
    /// invented price.
    /// </summary>
    public static class Village
    {
        public const int MaxLevel = 5;

        /// <summary>
        /// The highest level reachable by an <see cref="UpgradeCommand"/>. Lower than
        /// <see cref="MaxLevel"/>: the ladder defines level 5's cap and period so it can be modelled
        /// or drawn, but MW2 publishes no price to reach it and how it is reached at all is marked
        /// <c>[?]</c> in the reference (<c>MW2-RULES.md</c> §2.2) - granting it is a later feature,
        /// not this one.
        /// </summary>
        public const int MaxUpgradableLevel = 4;

        private static readonly int[] _garrisonCaps = { 20, 40, 60, 80, 100 };

        // 60/30/20/15/12 ticks at 50ms = 3.0/1.5/1.0/0.75/0.6 seconds per unit = 0.33/0.66/1.00/1.33/1.66 units/sec.
        private static readonly long[] _productionPeriodTicks = { 60, 30, 20, 15, 12 };

        // Indexed by the level being upgraded *from*, so [0] is the cost to reach level 2. The first
        // upgrade is deliberately affordable from the starting garrison of 10 without waiting, so
        // "grow first" is a live opening move rather than something a player only saves toward.
        private static readonly int[] _upgradeCosts = { 5, 10, 20 };

        // A dimensionless fraction of a base's drawn radius, not a pixel count - MW3.Core has no
        // notion of pixels (D-2). MW3.Game multiplies this by whatever radius the viewport produced.
        private static readonly double[] _ringThicknessFractionOfRadius = { 0.05, 0.10, 0.15, 0.20, 0.25 };

        public static int GarrisonCap(int level) => _garrisonCaps[IndexOfLevel(level, MaxLevel)];

        public static long ProductionPeriodTicks(int level) => _productionPeriodTicks[IndexOfLevel(level, MaxLevel)];

        public static int UpgradeCost(int fromLevel)
        {
            // Bounded by MaxUpgradableLevel, not MaxLevel: level 5 exists in the ladder but has no
            // published upgrade price, so "fromLevel 4" is already out of range for a cost lookup
            // even though it is a valid level for GarrisonCap/ProductionPeriodTicks.
            if (fromLevel < MinLevel || fromLevel >= MaxUpgradableLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromLevel),
                    fromLevel,
                    FormattableString.Invariant(
                        $"Village upgrade cost is defined only for levels {MinLevel} to {MaxUpgradableLevel - 1}."));
            }

            return _upgradeCosts[fromLevel - MinLevel];
        }

        public static double RingThicknessFractionOfRadius(int level) => _ringThicknessFractionOfRadius[IndexOfLevel(level, MaxLevel)];
    }

    /// <summary>
    /// The tower ladder: four levels, no garrison cap (towers never produce and are never a storage
    /// target for the cap concept), a flat 20-unit upgrade cost per level (<c>MW2-RULES.md</c> §2.3).
    /// </summary>
    public static class Tower
    {
        public const int MaxLevel = 4;

        /// <summary>
        /// The highest level reachable by an <see cref="UpgradeCommand"/> - the same as
        /// <see cref="MaxLevel"/>, since a tower's ladder has no unreachable top tier the way the
        /// village's does.
        /// </summary>
        public const int MaxUpgradableLevel = MaxLevel;

        private const int _upgradeCost = 20;

        private static readonly double[] _ringThicknessFractionOfRadius = { 0.05, 0.12, 0.19, 0.26 };

        public static int UpgradeCost(int fromLevel)
        {
            if (fromLevel < MinLevel || fromLevel >= MaxLevel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromLevel),
                    fromLevel,
                    FormattableString.Invariant($"Tower upgrade cost is defined only for levels {MinLevel} to {MaxLevel - 1}."));
            }

            return _upgradeCost;
        }

        public static double RingThicknessFractionOfRadius(int level) => _ringThicknessFractionOfRadius[IndexOfLevel(level, MaxLevel)];
    }

    /// <summary>
    /// The highest level <paramref name="type"/>'s ladder defines - cap, period, and ring-thickness
    /// lookups are valid up to here. Not necessarily reachable by upgrading; see
    /// <see cref="MaxUpgradableLevel(BaseType)"/> for the level a base actually stops upgrading at.
    /// </summary>
    public static int MaxLevel(BaseType type) => type switch
    {
        BaseType.Producer => Village.MaxLevel,
        BaseType.Tower => Tower.MaxLevel,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// The highest level <paramref name="type"/> can reach through an <see cref="UpgradeCommand"/>.
    /// A base at this level rejects further upgrades with <see cref="UpgradeOutcome.AlreadyAtMaxLevel"/>
    /// even though <see cref="MaxLevel(BaseType)"/> may be higher (the village ladder's level 5).
    /// </summary>
    public static int MaxUpgradableLevel(BaseType type) => type switch
    {
        BaseType.Producer => Village.MaxUpgradableLevel,
        BaseType.Tower => Tower.MaxUpgradableLevel,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// The garrison a base of this type and level produces up to, or null if this type has no cap
    /// (a tower: MW2 publishes no unit-capacity column for one, and no source gives one). It is a
    /// production ceiling, not a storage limit (D-21): arriving armies stack above it freely and
    /// nothing is ever destroyed for exceeding it - the base simply stops producing until it is back
    /// under. Every reader must handle the empty case explicitly; none substitutes a sentinel like 0
    /// or <see cref="int.MaxValue"/>.
    /// </summary>
    public static int? GarrisonCap(BaseType type, int level) => type switch
    {
        BaseType.Producer => Village.GarrisonCap(level),
        BaseType.Tower => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// Units it costs to raise a base of this type from <paramref name="fromLevel"/> to the next
    /// level. Only defined below that type's <see cref="MaxLevel(BaseType)"/> - a caller must reject
    /// an already-maxed base rather than ask what the impossible upgrade would cost.
    /// </summary>
    public static int UpgradeCost(BaseType type, int fromLevel) => type switch
    {
        BaseType.Producer => Village.UpgradeCost(fromLevel),
        BaseType.Tower => Tower.UpgradeCost(fromLevel),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// How thick a base's level ring is drawn, as a fraction of its radius - the only place a
    /// level's visible ring thickness is defined, so <c>MatchScreen</c> reads it rather than
    /// hardcoding one number per level.
    /// </summary>
    public static double RingThicknessFractionOfRadius(BaseType type, int level) => type switch
    {
        BaseType.Producer => Village.RingThicknessFractionOfRadius(level),
        BaseType.Tower => Tower.RingThicknessFractionOfRadius(level),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    private static int IndexOfLevel(int level, int maxLevel)
    {
        if (level < MinLevel || level > maxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                FormattableString.Invariant($"Level must be between {MinLevel} and {maxLevel}."));
        }

        return level - MinLevel;
    }
}
