namespace MW3.Core;

/// <summary>
/// Distinguishes acceptance of a <see cref="SendArmyCommand"/> from each rejection reason, so
/// <see cref="Match.Execute(SendArmyCommand)"/> never returns a bare bool or throws for an ordinary
/// rejection.
/// </summary>
public enum SendArmyOutcome
{
    Accepted,
    BaseNotFound,
    SourceNotOwnedByIssuer,
    SourceEqualsTarget,
    UnitCountNotPositive,
    UnitCountExceedsGarrison,
    MatchAlreadyDecided,
}
