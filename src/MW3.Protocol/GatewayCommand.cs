namespace MW3.Protocol;

/// <summary>
/// One thing a client asks its session to do (D-76). New protocol data rather than a copy of the
/// rules' own command records, and free to differ from them in two ways that matter.
///
/// It carries <b>no issuing player</b>. A gateway attributes every command it receives to its own
/// session's local player, so there is no field a client could set to submit on the AI's behalf. The
/// absence is the criterion; there is deliberately nothing here to test for.
///
/// A send carries a <see cref="SendStrength"/> and never a unit count. The count is a rule -
/// <c>SendStrengthCalculator</c> applied to the garrison at the tick the command applies - so
/// computing it client-side would be the client holding a copy of a rule that can drift from the
/// one the far side runs.
///
/// One record for all three kinds, with <see cref="Kind"/> selecting which fields are populated:
/// JSON has no discriminated union, and the same flattening is what
/// <see cref="PendingConstructionSnapshot"/> and <see cref="MatchEvent"/> already do.
/// </summary>
/// <param name="Kind">Which command this is - the field that says which of the rest are meaningful.</param>
/// <param name="FromBaseId">The base acted on: the source of a send, or the base upgraded or converted.</param>
/// <param name="ToBaseId">A send's target base, null for anything else.</param>
/// <param name="Strength">A send's share of the source garrison, null for anything else.</param>
/// <param name="TargetType">A conversion's destination type, null for anything else.</param>
public sealed record GatewayCommand(
    GatewayCommandKind Kind,
    int FromBaseId,
    int? ToBaseId,
    SendStrength? Strength,
    BaseType? TargetType)
{
    /// <summary>
    /// Which command this is. Validated here rather than left to the receiver: a malformed command
    /// deserialized from a wire would otherwise be discovered by whichever field the dispatcher
    /// happened to dereference first, a long way from where the bad payload arrived - the same
    /// reasoning <see cref="BaseSnapshot.AvailableActions"/> and
    /// <see cref="ArmySnapshot.PathWaypoints"/> already validate on.
    /// </summary>
    public GatewayCommandKind Kind { get; } = Validate(Kind, FromBaseId, ToBaseId, Strength, TargetType);

    /// <summary>A send of <paramref name="strength"/> of base <paramref name="from"/>'s garrison at base <paramref name="to"/>.</summary>
    public static GatewayCommand SendArmy(int from, int to, SendStrength strength) =>
        new(GatewayCommandKind.SendArmy, from, to, strength, TargetType: null);

    /// <summary>Raising base <paramref name="baseId"/> by one level.</summary>
    public static GatewayCommand Upgrade(int baseId) =>
        new(GatewayCommandKind.Upgrade, baseId, ToBaseId: null, Strength: null, TargetType: null);

    /// <summary>Converting base <paramref name="baseId"/> to <paramref name="targetType"/>.</summary>
    public static GatewayCommand Convert(int baseId, BaseType targetType) =>
        new(GatewayCommandKind.Convert, baseId, ToBaseId: null, Strength: null, targetType);

    private static GatewayCommandKind Validate(
        GatewayCommandKind kind,
        int fromBaseId,
        int? toBaseId,
        SendStrength? strength,
        BaseType? targetType)
    {
        switch (kind)
        {
            case GatewayCommandKind.SendArmy:
                if (toBaseId is null)
                {
                    throw new ArgumentException("A SendArmy command must name a target base.", nameof(toBaseId));
                }

                if (toBaseId == fromBaseId)
                {
                    throw new ArgumentException("A SendArmy command cannot target its own source base.", nameof(toBaseId));
                }

                if (strength is null)
                {
                    throw new ArgumentException("A SendArmy command must carry a send strength.", nameof(strength));
                }

                if (targetType is not null)
                {
                    throw new ArgumentException("A SendArmy command carries no target type.", nameof(targetType));
                }

                return kind;

            case GatewayCommandKind.Upgrade:
                if (toBaseId is not null)
                {
                    throw new ArgumentException("An Upgrade command carries no target base.", nameof(toBaseId));
                }

                if (strength is not null)
                {
                    throw new ArgumentException("An Upgrade command carries no send strength.", nameof(strength));
                }

                if (targetType is not null)
                {
                    throw new ArgumentException("An Upgrade command carries no target type.", nameof(targetType));
                }

                return kind;

            case GatewayCommandKind.Convert:
                if (targetType is null)
                {
                    throw new ArgumentException("A Convert command must name the type it converts to.", nameof(targetType));
                }

                if (toBaseId is not null)
                {
                    throw new ArgumentException("A Convert command carries no target base.", nameof(toBaseId));
                }

                if (strength is not null)
                {
                    throw new ArgumentException("A Convert command carries no send strength.", nameof(strength));
                }

                return kind;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown gateway command kind.");
        }
    }
}
