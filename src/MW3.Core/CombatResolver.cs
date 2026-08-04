namespace MW3.Core;

/// <summary>
/// Resolves arrival combat by MW2's <c>Bu = (a/d) × Wu</c> (<c>MW2-RULES.md</c> §4.1), where
/// <c>a</c> is the attacker's total attack index and <c>d</c> the defender's total protection index
/// (D-29). Takes the composed indices rather than reaching into a <see cref="Base"/> itself, so
/// <see cref="Match.ResolveArrival"/> holds no combat arithmetic of its own.
/// <para>
/// The capture decision is exact integer cross-multiplication with no division and no rounding:
/// algebraically identical to MW2's <c>Du − (a/d) × Wu &lt; 0</c>, and the only way to avoid a
/// naive floored division making an emptied building uncapturable (one unit against a 0-garrison
/// level-4 tower would floor to zero damage under <c>floor(Wu × a / d)</c>). Only the remainder -
/// never the decision - rounds, and it floors with a minimum of 1 so a base that changes hands is
/// never left at zero.
/// </para>
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// Morale's contribution to an attack or defence index, fixed at identity until parity gap G-1
    /// supplies a real value. A percentage, not a delta - 100 multiplies through as "no change".
    /// </summary>
    public const int MoraleContributionPercent = 100;

    /// <summary>
    /// A forge's contribution to an attack or defence index, fixed at identity until parity gap G-6
    /// supplies a real value.
    /// </summary>
    public const int ForgeContributionPercent = 100;

    /// <summary>
    /// The uniform baseline an attacking wave's units carry before morale and forge multipliers -
    /// MW3 has no per-unit attack stat, so every unit attacks at the same 100% until a later feature
    /// gives one side an edge.
    /// </summary>
    public const int BaselineAttackPercent = 100;

    /// <summary>
    /// The attacker's total attack index <c>a</c>: the uniform baseline composed with morale and
    /// forge, both fixed at identity this phase.
    /// </summary>
    public static int ComposeAttackerIndex() =>
        ComposePercentages(BaselineAttackPercent, MoraleContributionPercent, ForgeContributionPercent);

    /// <summary>
    /// The defender's total protection index <c>d</c>: the base's own defence percentage composed
    /// with morale and forge, both fixed at identity this phase.
    /// </summary>
    public static int ComposeDefenderIndex(int baseDefencePercent) =>
        ComposePercentages(baseDefencePercent, MoraleContributionPercent, ForgeContributionPercent);

    /// <summary>
    /// Whether <paramref name="attackingUnits"/> would capture a base defended by
    /// <paramref name="defendingGarrison"/>: <c>attackingUnits × attackerIndex &gt;
    /// defendingGarrison × defenderIndex</c> - strictly greater, so an exact tie leaves the
    /// defender holding zero. This is the single source of the capture decision - both
    /// <see cref="Resolve"/> (actual resolution) and <see cref="AiBrain"/>'s predictions (winnability
    /// and threat) go through it, so they can never quietly disagree, mirroring how
    /// <see cref="TravelTimeCalculator"/> is the one source of arrival timing for both.
    /// </summary>
    public static bool WouldCapture(int attackerIndex, int defenderIndex, int attackingUnits, int defendingGarrison) =>
        (long)attackingUnits * attackerIndex > (long)defendingGarrison * defenderIndex;

    /// <summary>
    /// Resolves one arriving wave against a defended garrison. The attacker captures the base iff
    /// <c>waveUnits × attackerIndex &gt; defendingGarrison × defenderIndex</c> - strictly greater, so
    /// an exact tie leaves the defender holding zero.
    /// </summary>
    public static CombatResult Resolve(int attackerIndex, int defenderIndex, int waveUnits, int defendingGarrison)
    {
        var attackPower = (long)waveUnits * attackerIndex;
        var defensePower = (long)defendingGarrison * defenderIndex;

        if (WouldCapture(attackerIndex, defenderIndex, waveUnits, defendingGarrison))
        {
            var remaining = (int)((attackPower - defensePower) / defenderIndex);
            return new CombatResult(Captured: true, RemainingGarrison: remaining < 1 ? 1 : remaining);
        }

        var held = (int)(defendingGarrison - (attackPower / defenderIndex));
        return new CombatResult(Captured: false, RemainingGarrison: held < 0 ? 0 : held);
    }

    // Composing three percentages that are each "100 = no change" this phase. Safe regardless of
    // whether a future multi-term case (G-1, G-6 both live) turns out to stack multiplicatively or
    // additively (MW2-RULES.md §4.3, [?]): with at most one non-identity term, multiplying by 100
    // for every other term changes nothing, so this composition does not itself answer the stacking
    // question - it only reduces correctly while the question stays unobserved.
    private static int ComposePercentages(int basePercent, int moralePercent, int forgePercent) =>
        (int)((long)basePercent * moralePercent * forgePercent / (100 * 100));
}
