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
/// <para>
/// Indices are expressed in <b>basis points</b> (1/10000 - FR-2), not percent: identity is
/// <c>10000</c>, not <c>100</c>. Percent scale floors a common case - a level-2 village (110%)
/// defended at morale 1 (125%) is 137.5, truncated to 137, a bias toward the attacker that can flip
/// a knife-edge capture. At basis-point scale, and with the forge term still at identity, the
/// two-term product is exact with no division loss at all; only a future third non-identity term
/// (G-6) floors, and then at 1/10000 grain rather than 1%.
/// </para>
/// </summary>
public static class CombatResolver
{
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
    /// The attacker's total attack index <c>a</c>, in basis points (1/10000): the uniform baseline
    /// composed with <paramref name="moraleAttackPercent"/> - the arriving army's owner's live
    /// morale attack percentage (<see cref="MoraleTable.AttackPercentage"/>), 100 for a morale-0
    /// owner - and forge, forge fixed at identity until G-6.
    /// </summary>
    public static int ComposeAttackerIndex(int moraleAttackPercent) =>
        ComposePercentages(BaselineAttackPercent, moraleAttackPercent, ForgeContributionPercent);

    /// <summary>
    /// The defender's total protection index <c>d</c>, in basis points (1/10000): the base's own
    /// defence percentage composed with <paramref name="moraleDefencePercent"/> - the base owner's
    /// morale defence percentage (<see cref="MoraleTable.DefencePercentage"/>), 100 for a neutral
    /// base, which has no morale (D-11) - and forge, forge fixed at identity until G-6.
    /// </summary>
    public static int ComposeDefenderIndex(int baseDefencePercent, int moraleDefencePercent) =>
        ComposePercentages(baseDefencePercent, moraleDefencePercent, ForgeContributionPercent);

    /// <summary>
    /// Whether <paramref name="attackingUnits"/> would capture a base defended by
    /// <paramref name="defendingGarrison"/>: <c>attackingUnits × attackerIndex &gt;
    /// defendingGarrison × defenderIndex</c> - strictly greater, so an exact tie leaves the
    /// defender holding zero. This is the single source of the capture decision - both
    /// <see cref="Resolve"/> (actual resolution) and <see cref="AiBrain"/>'s predictions (winnability
    /// and threat) go through it, so they can never quietly disagree, mirroring how
    /// <see cref="TravelTimeCalculator"/> is the one source of arrival timing for both. Indices are
    /// basis points, the same scale <see cref="Resolve"/> takes.
    /// </summary>
    public static bool WouldCapture(int attackerIndex, int defenderIndex, int attackingUnits, int defendingGarrison) =>
        (long)attackingUnits * attackerIndex > (long)defendingGarrison * defenderIndex;

    /// <summary>
    /// Resolves one arriving wave against a defended garrison. The attacker captures the base iff
    /// <c>waveUnits × attackerIndex &gt; defendingGarrison × defenderIndex</c> - strictly greater, so
    /// an exact tie leaves the defender holding zero. Indices are basis points (1/10000).
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

    // Composes three percentages into a basis-point (1/10000) index. D-40 settles this
    // multiplicatively - MW2-RULES.md §4.3 flags stacking as [?] ("the sources say only that the
    // terms combine"), but the reference's own worked example multiplies, and this is the codebase's
    // shipped composition; MW2-PARITY.md records this as MW3's assumption, not a parity claim, so a
    // future observation of additive stacking in MW2 reopens the gap. Dividing once by 100 (not
    // 100*100) lands identity (100, 100, 100) on exactly 10000, and - since the forge term stays at
    // identity this phase - the two-term product basePercent*moralePercent is exact with no
    // remainder discarded; only a future non-identity forge term (G-6) can floor, and then at
    // 1/10000 grain.
    private static int ComposePercentages(int basePercent, int moralePercent, int forgePercent) =>
        (int)((long)basePercent * moralePercent * forgePercent / 100);
}
