namespace MW3.Core;

/// <summary>
/// One action a base's owner could take on it right now, its unit cost, and whether it is
/// affordable - the pure answer <see cref="Match.AvailableActions"/> returns for the widget to
/// render rather than compute itself (D-25). <see cref="Cost"/> is exactly 0 when
/// <see cref="Availability"/> is <see cref="BaseActionAvailability.AlreadyAtMaxLevel"/>, since there
/// is no next level left to price. <see cref="ConvertTargetType"/> is null for
/// <see cref="BaseActionKind.Upgrade"/> and set to one of the <see cref="BaseType"/>s other than the
/// base's own for <see cref="BaseActionKind.Convert"/> - the action carries its own target so the
/// widget never picks one itself (D-25, D-48, FR-5).
/// </summary>
public sealed record BaseAction(BaseActionKind Kind, int Cost, BaseActionAvailability Availability, BaseType? ConvertTargetType = null);
