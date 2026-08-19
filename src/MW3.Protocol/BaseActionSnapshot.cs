namespace MW3.Protocol;

/// <summary>
/// One action the local player could take on one of their bases, and whether it can be taken right
/// now - the same answer <c>Match.AvailableActions</c> gives, shipped so the client can grey a
/// button for the right reason without knowing a single rule (D-25, D-66). The server keeps
/// deciding: a client that believes an action affordable can still have the command rejected, and
/// FR-3's gateway carries a command result for exactly that reason.
/// </summary>
/// <param name="Kind">Upgrade, or convert to <paramref name="ConvertTargetType"/>.</param>
/// <param name="Cost">Units the action costs, exactly 0 when there is no next level to price.</param>
/// <param name="Availability">Why the action can or cannot be taken (D-25).</param>
/// <param name="ConvertTargetType">
/// What a convert action converts to, null for an upgrade. The action carries its own target so the
/// widget never picks one (D-48).
/// </param>
public sealed record BaseActionSnapshot(
    BaseActionKind Kind,
    int Cost,
    BaseActionAvailability Availability,
    BaseType? ConvertTargetType);
