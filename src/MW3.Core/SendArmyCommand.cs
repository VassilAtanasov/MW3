namespace MW3.Core;

/// <summary>
/// The only mutation input besides <see cref="Match.Advance"/> (D-12). Carries an explicit unit
/// count rather than implying "half" - choosing a count is the caller's policy, not a rule.
/// </summary>
public sealed record SendArmyCommand(Player IssuingPlayer, int SourceBaseId, int TargetBaseId, int UnitCount);
