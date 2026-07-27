namespace MW3.Core;

/// <summary>
/// Owns a <see cref="Match"/> and the AI's <see cref="IPlayerBrain"/> - the only thing that
/// consults the brain and submits its commands (D-16). Slices <see cref="Advance"/> so every
/// decision tick is hit exactly once, however the caller chunks the ticks handed to it, by always
/// measuring the next decision tick from the match's own <see cref="Match.ElapsedTicks"/> rather
/// than from anything the caller tracks (D-12).
/// </summary>
public sealed class MatchRunner
{
    /// <summary>
    /// How often the AI decides: the first decision is at this tick, then every multiple of it.
    /// </summary>
    public const long DecisionIntervalTicks = 20;

    private readonly Match _match;
    private readonly IPlayerBrain _aiBrain;

    public MatchRunner(Match match, IPlayerBrain aiBrain)
    {
        if (match is null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        if (aiBrain is null)
        {
            throw new ArgumentNullException(nameof(aiBrain));
        }

        _match = match;
        _aiBrain = aiBrain;
    }

    public Match Match => _match;

    /// <summary>Submits a command - the only path either the human or the AI's commands take.</summary>
    public SendArmyOutcome Execute(SendArmyCommand command) => _match.Execute(command);

    /// <summary>
    /// Advances the match by <paramref name="ticks"/> whole ticks, stopping at every decision tick
    /// crossed to let the AI brain decide, before continuing to the requested total.
    /// </summary>
    public void Advance(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Ticks cannot be negative.");
        }

        var targetElapsedTicks = _match.ElapsedTicks + ticks;

        while (true)
        {
            var nextDecisionTick = NextDecisionTickAfter(_match.ElapsedTicks);
            if (nextDecisionTick > targetElapsedTicks)
            {
                _match.Advance(targetElapsedTicks - _match.ElapsedTicks);
                return;
            }

            _match.Advance(nextDecisionTick - _match.ElapsedTicks);

            var decision = _aiBrain.Decide(_match);
            if (decision.HasCommand)
            {
                _match.Execute(decision.Command);
            }
        }
    }

    private static long NextDecisionTickAfter(long elapsedTicks) =>
        ((elapsedTicks / DecisionIntervalTicks) + 1) * DecisionIntervalTicks;
}
