namespace MW3.Core;

/// <summary>
/// The level ladder (D-22): the single source for every per-level tuning number, so the simulation,
/// the AI, and the tests read one table rather than each hardcoding a value. A short fixed ladder
/// rather than a formula, because every interesting ladder is non-linear and a formula invites
/// tuning by exponent; a table in code rather than a content file, because this phase keeps one map
/// and one ruleset hardcoded (REQUIREMENTS.md §6).
/// <para>
/// <b>Superseded in part by D-29 (FR-3b, 29-07-2026):</b> a level no longer buys economy only. It
/// also buys defence, read by <see cref="CombatResolver"/> rather than left at phase 2's plain 1:1
/// arithmetic (D-15). The claim below is retained as the record of D-22's original reasoning, not as
/// a rule still in force.
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
    /// Ticks a conversion takes to complete (D-30, FR-3c), identical in both directions. 100 ticks at
    /// the 50 ms tick is 5 seconds (<c>MW2-RULES.md</c> §2.2, §2.3 - the Time column is identical for
    /// villages and towers).
    /// </summary>
    public const long ConversionBuildDurationTicks = 100;

    /// <summary>
    /// The recapture grace window (D-30, FR-3c, <c>MW2-RULES.md</c> §2.5): a capture that retakes a
    /// base from the player who held it immediately before its last owner change, within this many
    /// ticks of that change, skips the usual one-level demotion. 20 ticks at the 50 ms tick is 1 second.
    /// </summary>
    public const long RecaptureGraceTicks = 20;

    // Ticks to raise a base from fromLevel to fromLevel+1, indexed by fromLevel - MinLevel. Identical
    // for villages and towers (MW2-RULES.md §2.2, §2.3): 100/200/300 ticks = 5/10/15 seconds for
    // levels 2/3/4. Both ladders' MaxUpgradableLevel tops out at 4, so fromLevel never exceeds 3 here.
    private static readonly long[] _upgradeBuildDurationTicks = { 100, 200, 300 };

    /// <summary>
    /// Ticks an upgrade from <paramref name="fromLevel"/> takes to complete (D-30, FR-3c). Shared by
    /// both ladders - MW2 publishes the same Time column for villages and towers - so unlike
    /// <see cref="UpgradeCost(BaseType, int)"/> this is not split per type.
    /// </summary>
    public static long UpgradeBuildDurationTicks(int fromLevel)
    {
        if (fromLevel < MinLevel || fromLevel > MinLevel + _upgradeBuildDurationTicks.Length - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fromLevel),
                fromLevel,
                FormattableString.Invariant(
                    $"Upgrade build duration is defined only for levels {MinLevel} to {MinLevel + _upgradeBuildDurationTicks.Length - 1}."));
        }

        return _upgradeBuildDurationTicks[fromLevel - MinLevel];
    }

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

        // +10 percentage points per level (MW2-RULES.md §2.2), D-29.
        private static readonly int[] _defencePercentages = { 100, 110, 120, 130, 140 };

        // Indexed by the level being upgraded *from*, so [0] is the cost to reach level 2. The first
        // upgrade is deliberately affordable from the starting garrison of 10 without waiting, so
        // "grow first" is a live opening move rather than something a player only saves toward.
        private static readonly int[] _upgradeCosts = { 5, 10, 20 };

        // A dimensionless fraction of a base's drawn radius, not a pixel count - MW3.Core has no
        // notion of pixels (D-2). MW3.Game multiplies this by whatever radius the viewport produced.
        private static readonly double[] _ringThicknessFractionOfRadius = { 0.05, 0.10, 0.15, 0.20, 0.25 };

        public static int GarrisonCap(int level) => _garrisonCaps[IndexOfLevel(level, MaxLevel)];

        public static long ProductionPeriodTicks(int level) => _productionPeriodTicks[IndexOfLevel(level, MaxLevel)];

        public static int DefencePercentage(int level) => _defencePercentages[IndexOfLevel(level, MaxLevel)];

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

        // 140/170/190/200% (MW2-RULES.md §2.3), D-29 - a level-1 tower already matches a level-5
        // village, which is what makes a tower a defensive structure rather than one that trades
        // production for range.
        private static readonly int[] _defencePercentages = { 140, 170, 190, 200 };

        // 100/110/125/140% of a level-1 anchor of 0.20 (MW2-RULES.md §2.3's published radius
        // *ratios* applied to an MW3-chosen base, parity G-22) - normalized map units, D-2. No range
        // at any level reaches a start base on any of the three shipped maps (asserted in
        // LevelTableTests and MapCatalogTests); the closest base-to-base distance among them is
        // Big's tower-to-forge pair at 0.18 (MapCatalogTests.Big_EachNeutralTower_CoversTheNeutralForge_AtLevelOne).
        private static readonly double[] _rangeUnits = { 0.20, 0.22, 0.25, 0.28 };

        // MW3's own numbers (parity G-13): MW2 never publishes tower damage or rate of fire, and
        // MW2-RULES.md §2.3's "shooting speed" column is marked [?] and is most likely projectile
        // speed, not fire rate - unusable as a tuning input. One unit removed per shot (FR-4).
        private static readonly long[] _firePeriodTicks = { 6, 5, 4, 3 };

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

        public static int DefencePercentage(int level) => _defencePercentages[IndexOfLevel(level, MaxLevel)];

        /// <summary>The range, in normalized map units, within which this level's tower fires at an enemy army (FR-4).</summary>
        public static double RangeUnits(int level) => _rangeUnits[IndexOfLevel(level, MaxLevel)];

        /// <summary>Ticks between shots at this level, once a target is in range (FR-4). One unit removed per shot.</summary>
        public static long FirePeriodTicks(int level) => _firePeriodTicks[IndexOfLevel(level, MaxLevel)];
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
        BaseType.Forge => MinLevel,
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
        BaseType.Forge => MinLevel,
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
        BaseType.Forge => null,
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
        BaseType.Forge => throw new ArgumentOutOfRangeException(
            nameof(fromLevel),
            fromLevel,
            "Forge has no upgrade path."),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// The percentage of the flat 1:1 baseline this base defends at (D-29): 100 at a level-1
    /// village, rising to 140 for a fully upgraded one, and 140 to 200 across the tower ladder - so a
    /// level-1 tower already defends as well as a level-5 village. Read by
    /// <see cref="CombatResolver"/>, never applied inline at the arrival site.
    /// </summary>
    public static int DefencePercentage(BaseType type, int level) => type switch
    {
        BaseType.Producer => Village.DefencePercentage(level),
        BaseType.Tower => Tower.DefencePercentage(level),
        BaseType.Forge => 100,
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
        BaseType.Forge => 0.05,
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
