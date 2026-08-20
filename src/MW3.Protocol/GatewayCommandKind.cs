namespace MW3.Protocol;

/// <summary>
/// Which command a <see cref="GatewayCommand"/> is. JSON has no discriminated union, so the kind
/// selects which of the command's optional fields are populated - the same flattening
/// <see cref="PendingConstructionSnapshot"/> and <see cref="MatchEvent"/> already use, rather than a
/// third shape for the same problem.
/// </summary>
public enum GatewayCommandKind
{
    /// <summary>Send a share of one base's garrison at another base.</summary>
    SendArmy,

    /// <summary>Raise one owned base by one level.</summary>
    Upgrade,

    /// <summary>Convert one owned base to another type.</summary>
    Convert,
}
