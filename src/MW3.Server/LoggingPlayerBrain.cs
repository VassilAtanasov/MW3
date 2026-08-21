using MW3.Core;

namespace MW3.Server;

/// <summary>
/// Wraps an <see cref="IPlayerBrain"/> so every command it decides is logged before <c>Decide</c>
/// returns it (FR-6, D-87). This is the only way to observe the two command paths that bypass the
/// gateway entirely: <see cref="MatchRunner.Advance"/>'s call for the opponent AI, and
/// <see cref="MatchSession.AdvanceInterleavingSubstitute"/>'s call for the disconnect substitute.
/// <see cref="MatchSession"/> wraps both - the substitute at the point it is lazily constructed,
/// not only in the session's own constructor, since a decorator applied only there would miss
/// exactly the abandoned-match stretch this feature exists to record (D-87a).
///
/// Needs no <c>MW3.Core</c> change: one decorator shape covers both brains only because phase 2's
/// D-16 already made <see cref="MatchRunner"/> the single command path.
/// </summary>
internal sealed class LoggingPlayerBrain : IPlayerBrain
{
    private readonly IPlayerBrain _inner;
    private readonly MatchLogWriter _writer;

    internal LoggingPlayerBrain(IPlayerBrain inner, MatchLogWriter writer)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    public Player Player => _inner.Player;

    /// <inheritdoc />
    public BrainDecision Decide(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var decision = _inner.Decide(match);
        if (decision.HasCommand)
        {
            // No verdict is logged: MatchRunner.Advance and MatchSession.AdvanceInterleavingSubstitute
            // both execute this decision directly and return its outcome to nobody this decorator can
            // observe, so absence is modelled rather than a verdict guessed (D-87, docs/CONVENTIONS.md).
            _writer.WriteBrainCommand(match.ElapsedTicks, Player.Id, ToGatewayCommand(decision), SendUnitCountOf(decision));
        }

        return decision;
    }

    /// <summary>
    /// Reshapes the brain's decision to the wire's <see cref="GatewayCommand"/>, so both command
    /// sources share one logged shape. Every send the AI decides commits
    /// <see cref="SendStrength.Half"/> of the source garrison - the only strength
    /// <see cref="AiBrain"/> ever passes to <c>SendStrengthCalculator.Compute</c> - so the strength
    /// is the one value the brain actually used, never guessed. The exact unit count it actually
    /// committed travels separately (see <see cref="SendUnitCountOf"/>) rather than being left for a
    /// replay to recompute from this strength.
    /// </summary>
    private static GatewayCommand ToGatewayCommand(BrainDecision decision)
    {
        if (decision.IsUpgrade)
        {
            return GatewayCommand.Upgrade(decision.Upgrade.BaseId);
        }

        if (decision.IsConvert)
        {
            return GatewayCommand.Convert(decision.Convert.BaseId, decision.Convert.TargetType);
        }

        return GatewayCommand.SendArmy(decision.Command.SourceBaseId, decision.Command.TargetBaseId, SendStrength.Half);
    }

    /// <summary>The exact unit count a send decision carries, or null for an upgrade or convert (D-89).</summary>
    private static int? SendUnitCountOf(BrainDecision decision) => decision.IsSend ? decision.Command.UnitCount : null;
}
