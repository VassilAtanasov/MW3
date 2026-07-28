namespace MW3.Core;

/// <summary>
/// Distinguishes acceptance of an <see cref="UpgradeCommand"/> from each rejection reason, so
/// <see cref="Match.Execute(UpgradeCommand)"/> never returns a bare bool or throws for an ordinary
/// rejection. Mirrors <see cref="SendArmyOutcome"/> rather than inventing a second shape.
/// </summary>
public enum UpgradeOutcome
{
    Accepted,
    BaseNotFound,
    BaseNotOwnedByIssuer,
    AlreadyAtMaxLevel,
    GarrisonBelowCost,
    MatchAlreadyDecided,
}
