namespace MW3.Core;

/// <summary>
/// The forge ladder (D-22): the single source for every forge percentage number, mirroring
/// <see cref="LevelTable"/>'s role for the level ladder and <see cref="MoraleTable"/>'s for morale.
/// No call site outside this class names a forge percentage literal, and none names the cap
/// (<c>docs/forges/REQUIREMENTS.md</c> §4 "Tuning values", <c>MW2-RULES.md</c> §2.4, <c>[T]</c>).
/// <para>
/// Unlike the other two tables this one is indexed by a <i>count</i>, not a level: a forge has
/// exactly one tier and no position component (<c>MW2-RULES.md</c> §2, §2.4), so all that matters
/// is how many of them a player holds. The buff is global - it applies to every attack that
/// player's units make and every base that player defends, anywhere on the map - which is what
/// makes trading a producer for a forge pay.
/// </para>
/// </summary>
public static class ForgeTable
{
    /// <summary>
    /// The count beyond which a further forge contributes nothing (<c>MW2-RULES.md</c> §2.4). Holding
    /// a fifth forge is legal play, not an error, so <see cref="AttackPercentage"/> and
    /// <see cref="DefencePercentage"/> clamp here rather than throwing - see their own remarks.
    /// </summary>
    public const int MaxContributingForges = 4;

    /// <summary>
    /// MW2's published rule of thumb for how many producers justify another forge
    /// (<c>MW2-RULES.md</c> §2.4: "one forge per four unit-producing buildings"). Read by
    /// <see cref="AiBrain"/>'s convert clause to decide whether a forge is owed right now; equal to
    /// <see cref="MaxContributingForges"/> by coincidence only - one is a build ratio, the other a
    /// buff cap, and neither call site names the other's literal (D-22).
    /// </summary>
    public const int ProducersPerForge = 4;

    /// <summary>The count at which the forge term is identity - no forges held, no buff.</summary>
    public const int MinForgeCount = 0;

    // Percentages indexed by forge count, [0] = no forges = identity. MW2-RULES.md §2.4, [T].
    private static readonly int[] _defencePercentages = { 100, 125, 135, 145, 150 };
    private static readonly int[] _attackPercentages = { 100, 150, 175, 190, 200 };

    /// <summary>
    /// The defence percentage <paramref name="forgeCount"/> forges contribute to their owner's
    /// protection index, everywhere on the map. Clamps at <see cref="MaxContributingForges"/>: a
    /// fifth forge is a legal holding that simply buys nothing, so this returns the four-forge value
    /// rather than throwing. Throws only for a negative count, which is a caller bug.
    /// </summary>
    public static int DefencePercentage(int forgeCount) => _defencePercentages[IndexOfCount(forgeCount)];

    /// <summary>
    /// The attack percentage <paramref name="forgeCount"/> forges contribute to their owner's attack
    /// index, everywhere on the map. Clamps at <see cref="MaxContributingForges"/> on the same terms
    /// as <see cref="DefencePercentage"/>, and throws only for a negative count.
    /// </summary>
    public static int AttackPercentage(int forgeCount) => _attackPercentages[IndexOfCount(forgeCount)];

    private static int IndexOfCount(int forgeCount)
    {
        if (forgeCount < MinForgeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(forgeCount),
                forgeCount,
                FormattableString.Invariant($"A forge count cannot be below {MinForgeCount}."));
        }

        // Clamp, do not throw: the cap is a rule about what a forge buys, not a bound on how many a
        // player may hold.
        return forgeCount > MaxContributingForges ? MaxContributingForges : forgeCount;
    }
}
