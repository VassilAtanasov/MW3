namespace MW3.Core;

/// <summary>
/// Core-side AI seam (D-16): decides at most one command per decision for the player it acts for,
/// reading only <see cref="Match"/> state that is already publicly exposed and never calling
/// <see cref="Match.Execute(SendArmyCommand)"/> itself - only <see cref="MatchRunner"/> submits a
/// brain's decision.
/// </summary>
public interface IPlayerBrain
{
    /// <summary>The player this brain decides for.</summary>
    Player Player { get; }

    /// <summary>
    /// Decides what, if anything, <see cref="Player"/> should do right now. Called only on
    /// decision ticks (<see cref="MatchRunner.DecisionIntervalTicks"/>); reads <paramref name="match"/>
    /// but never mutates it.
    /// </summary>
    BrainDecision Decide(Match match);
}
