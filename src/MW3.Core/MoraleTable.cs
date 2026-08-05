namespace MW3.Core;

/// <summary>
/// The morale ladder and its gain/loss/upgrade tables (D-22): the single source for every morale
/// tuning number, mirroring <see cref="LevelTable"/>'s role for the level ladder. No call site
/// outside this class names a morale literal (D-37, D-38, D-41,
/// <c>docs/morale/REQUIREMENTS.md</c> §4 "Tuning values"). Villages and towers are separate tables,
/// exactly as <see cref="LevelTable"/> splits them, because MW2 publishes different capture and
/// upgrade values per building type. Forge rows are deliberately absent - MW3 has no forge (parity
/// G-6).
/// </summary>
public static class MoraleTable
{
    /// <summary>The lowest a player's morale points can fall to (D-38).</summary>
    public const int PointFloor = 0;

    /// <summary>
    /// The highest a player's morale points can rise to - the level-5 threshold. Not published by
    /// MW2; chosen so decay (FR-3) can never be outrun by banking points indefinitely (D-38).
    /// </summary>
    public const int PointCeiling = 8000;

    /// <summary>The lowest morale level, always reachable, needing no points.</summary>
    public const int MinLevel = 0;

    /// <summary>The highest morale level, reached at <see cref="PointCeiling"/> points.</summary>
    public const int MaxLevel = 5;

    /// <summary>Morale points awarded for destroying one enemy attacking unit (MW2-RULES.md §5.2).</summary>
    public const int AttackingUnitDestroyedGain = 10;

    /// <summary>Morale points lost for one of your own units dying while attacking (MW2-RULES.md §5.3).</summary>
    public const int AttackingUnitDiedLoss = 10;

    /// <summary>
    /// The inactivity decay period (FR-3, D-38): whole points are lost on this tick boundary, never
    /// fractionally per tick. 20 ticks is exactly one second at the 50 ms tick rate (D-27), since the
    /// published decay rates are per-second (<c>docs/morale/REQUIREMENTS.md</c> §4).
    /// </summary>
    public const int DecayPeriodTicks = 20;

    // Points required to reach each level, indexed by level (index 0 = level 0 = no threshold).
    // MW2-RULES.md §5.1, [T].
    private static readonly int[] _pointThresholds = { 0, 500, 1000, 2000, 4000, 8000 };

    // Percentages per level, indexed by level. MW2-RULES.md §5.1, [T].
    private static readonly int[] _defencePercentages = { 100, 125, 150, 175, 200, 225 };
    private static readonly int[] _attackPercentages = { 100, 105, 110, 115, 120, 125 };
    private static readonly int[] _unitSpeedPercentages = { 100, 110, 120, 130, 140, 150 };

    // Idle ticks before decay starts, indexed by level (FR-3, MW2-RULES.md §5.4, [T]).
    private static readonly int[] _decayThresholdTicks = { 200, 180, 160, 140, 120, 100 };

    // Points lost per decay period, indexed by level (FR-3, MW2-RULES.md §5.4, [T]).
    private static readonly int[] _decayPointsPerPeriod = { 10, 20, 25, 50, 100, 200 };

    /// <summary>Clamps <paramref name="points"/> to <c>[<see cref="PointFloor"/>, <see cref="PointCeiling"/>]</c> (D-38).</summary>
    public static int ClampPoints(int points) => Math.Clamp(points, PointFloor, PointCeiling);

    /// <summary>
    /// The 0-5 sun level for <paramref name="points"/>: the highest threshold reached, never stored
    /// separately (D-38). Exactly <see cref="PointCeiling"/> lands on level <see cref="MaxLevel"/>.
    /// </summary>
    public static int LevelForPoints(int points)
    {
        var level = MinLevel;
        for (var l = MinLevel + 1; l <= MaxLevel; l++)
        {
            if (points >= _pointThresholds[l])
            {
                level = l;
            }
        }

        return level;
    }

    /// <summary>The points required to reach <paramref name="level"/>. Level 0 requires 0.</summary>
    public static int PointsThreshold(int level) => _pointThresholds[IndexOfLevel(level)];

    /// <summary>The defence percentage morale contributes at <paramref name="level"/> (read by FR-2).</summary>
    public static int DefencePercentage(int level) => _defencePercentages[IndexOfLevel(level)];

    /// <summary>The attack percentage morale contributes at <paramref name="level"/> (read by FR-2).</summary>
    public static int AttackPercentage(int level) => _attackPercentages[IndexOfLevel(level)];

    /// <summary>The unit speed percentage morale contributes at <paramref name="level"/> (read by FR-4).</summary>
    public static int UnitSpeedPercentage(int level) => _unitSpeedPercentages[IndexOfLevel(level)];

    /// <summary>
    /// Idle ticks (since the player's last accepted send) required before decay starts at
    /// <paramref name="level"/> - re-read from the level a player is at on every decay period, so the
    /// bleed self-slows as it drops them (FR-3, D-38).
    /// </summary>
    public static int DecayThresholdTicks(int level) => _decayThresholdTicks[IndexOfLevel(level)];

    /// <summary>
    /// Morale points lost on one decay period at <paramref name="level"/> (FR-3, D-38), applied whole
    /// rather than fractionally per tick.
    /// </summary>
    public static int DecayPointsPerPeriod(int level) => _decayPointsPerPeriod[IndexOfLevel(level)];

    /// <summary>
    /// Morale awarded to the capturer of a base of <paramref name="type"/> at <paramref name="level"/>
    /// - the level it held immediately before capture-demotion applies. <paramref name="wasOpponentOwned"/>
    /// selects the opponent table (higher reward) over the neutral one.
    /// </summary>
    public static int CaptureGain(BaseType type, int level, bool wasOpponentOwned) => type switch
    {
        BaseType.Producer => Village.CaptureGain(level, wasOpponentOwned),
        BaseType.Tower => Tower.CaptureGain(level, wasOpponentOwned),

        // Pinned at 0 pending phase 6 FR-4, which adds MW2-RULES.md §5.2/§5.3's forge capture rows -
        // mirrors CombatResolver.ForgeContributionPercent's identity pin since phase 3 FR-3b. A forge
        // must still be capturable in FR-1 (its type and single tier survive capture unchanged); it
        // simply scores no morale for doing so until FR-4 populates the table.
        BaseType.Forge => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// Morale lost by the previous owner of a base of <paramref name="type"/> at <paramref name="level"/>
    /// when it is captured - the level it held immediately before capture-demotion applies. Never
    /// called for a neutral base, which has no previous owner to charge.
    /// </summary>
    public static int CaptureLoss(BaseType type, int level) => type switch
    {
        BaseType.Producer => Village.CaptureLoss(level),
        BaseType.Tower => Tower.CaptureLoss(level),

        // Pinned at 0 pending phase 6 FR-4 - see the matching comment on CaptureGain above.
        BaseType.Forge => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    /// <summary>
    /// Morale awarded to the owner of a base of <paramref name="type"/> whose upgrade completes,
    /// reaching <paramref name="toLevel"/>. "To level 1" is recorded but unreachable - no upgrade
    /// ever produces a level-1 building, since every base starts there.
    /// </summary>
    public static int UpgradeGain(BaseType type, int toLevel) => type switch
    {
        BaseType.Producer => Village.UpgradeGain(toLevel),
        BaseType.Tower => Tower.UpgradeGain(toLevel),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown base type."),
    };

    private static int IndexOfLevel(int level)
    {
        if (level < MinLevel || level > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                FormattableString.Invariant($"Morale level must be between {MinLevel} and {MaxLevel}."));
        }

        return level;
    }

    /// <summary>The village capture/upgrade tables: five capturable levels, four upgradable "to" rows (MW2-RULES.md §5.2, §5.3).</summary>
    public static class Village
    {
        private static readonly int[] _neutralCaptureGain = { 40, 100, 160, 220, 300 };
        private static readonly int[] _opponentCaptureGain = { 100, 250, 400, 550, 750 };
        private static readonly int[] _captureLoss = { 50, 120, 200, 280, 380 };

        // Indexed by (toLevel - 1): [0] = to level 1 (unreachable, recorded per the kickoff settlement).
        private static readonly int[] _upgradeGain = { 50, 100, 150, 200 };

        public static int CaptureGain(int level, bool wasOpponentOwned) =>
            (wasOpponentOwned ? _opponentCaptureGain : _neutralCaptureGain)[IndexOfCaptureLevel(level, 5)];

        public static int CaptureLoss(int level) => _captureLoss[IndexOfCaptureLevel(level, 5)];

        public static int UpgradeGain(int toLevel) => _upgradeGain[IndexOfUpgradeLevel(toLevel)];
    }

    /// <summary>The tower capture/upgrade tables: four capturable levels, four upgradable "to" rows (MW2-RULES.md §5.2, §5.3).</summary>
    public static class Tower
    {
        private static readonly int[] _neutralCaptureGain = { 80, 200, 320, 440 };
        private static readonly int[] _opponentCaptureGain = { 200, 500, 800, 1100 };
        private static readonly int[] _captureLoss = { 100, 250, 400, 550 };

        // Indexed by (toLevel - 1): [0] = to level 1 (unreachable, recorded per the kickoff settlement).
        private static readonly int[] _upgradeGain = { 100, 200, 300, 400 };

        public static int CaptureGain(int level, bool wasOpponentOwned) =>
            (wasOpponentOwned ? _opponentCaptureGain : _neutralCaptureGain)[IndexOfCaptureLevel(level, 4)];

        public static int CaptureLoss(int level) => _captureLoss[IndexOfCaptureLevel(level, 4)];

        public static int UpgradeGain(int toLevel) => _upgradeGain[IndexOfUpgradeLevel(toLevel)];
    }

    private static int IndexOfCaptureLevel(int level, int maxLevel)
    {
        if (level < LevelTable.MinLevel || level > maxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                FormattableString.Invariant($"Capture level must be between {LevelTable.MinLevel} and {maxLevel}."));
        }

        return level - LevelTable.MinLevel;
    }

    private static int IndexOfUpgradeLevel(int toLevel)
    {
        // "To level 1" is row [0] - unreachable in play (every base starts at level 1) but modelled
        // rather than hidden, per the kickoff settlement recorded in docs/morale/REQUIREMENTS.md FR-1.
        if (toLevel < 1 || toLevel > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toLevel),
                toLevel,
                "Upgrade morale is defined only for resulting levels 1 to 4.");
        }

        return toLevel - 1;
    }
}
