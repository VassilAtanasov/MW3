namespace MW3.Core;

/// <summary>
/// Distinguishes acceptance of a <see cref="ConvertCommand"/> from each rejection reason, so
/// <see cref="Match.Execute(ConvertCommand)"/> never returns a bare bool or throws for an ordinary
/// rejection. Mirrors <see cref="UpgradeOutcome"/> and <see cref="SendArmyOutcome"/> rather than
/// inventing a second shape.
/// </summary>
public enum ConvertOutcome
{
    Accepted,
    BaseNotFound,
    BaseNotOwnedByIssuer,
    AlreadyOfTargetType,
    GarrisonBelowCost,
    MatchAlreadyDecided,
}
