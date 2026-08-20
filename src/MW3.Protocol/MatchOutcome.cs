namespace MW3.Protocol;

/// <summary>
/// The state of a match: undecided, or decided in one player's favor. Exposed read-only on
/// <c>Match</c> and changed only inside <c>Match.Advance</c> (D-13). Stays two-player here even
/// though <see cref="MatchSnapshot"/>'s player list is not - making the rules N-player belongs to
/// the Multiplayer project, not to this phase.
/// </summary>
public enum MatchOutcome
{
    InProgress,
    HumanVictory,
    HumanDefeat,
}
