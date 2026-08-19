namespace MW3.Protocol;

/// <summary>
/// One kind of action a base's owner can take on it: <see cref="Upgrade"/> raises its level;
/// <see cref="Convert"/> (FR-5) changes it to the target named on the action itself, one of the
/// <see cref="BaseType"/>s other than the base's own (D-48).
/// </summary>
public enum BaseActionKind
{
    Upgrade,
    Convert,
}
