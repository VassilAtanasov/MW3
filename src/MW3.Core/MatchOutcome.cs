namespace MW3.Core;

/// <summary>
/// The state of a <see cref="Match"/>: undecided, or decided in one player's favor. Exposed
/// read-only on <see cref="Match"/> and changed only inside <see cref="Match.Advance"/> (D-13).
/// </summary>
public enum MatchOutcome
{
    InProgress,
    HumanVictory,
    HumanDefeat,
}
