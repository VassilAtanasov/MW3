namespace MW3.Core;

/// <summary>
/// One kind of action a base's owner can take on it: <see cref="Upgrade"/> raises its level;
/// <see cref="Convert"/> (FR-5) flips it between <see cref="BaseType.Producer"/> and
/// <see cref="BaseType.Tower"/>.
/// </summary>
public enum BaseActionKind
{
    Upgrade,
    Convert,
}
