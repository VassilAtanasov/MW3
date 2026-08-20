using MW3.Core;

namespace MW3.Game.Tests;

/// <summary>
/// An <see cref="IMatchGateway"/> over a <see cref="Match"/> the test still holds a reference to.
/// <see cref="LoopbackMatchGateway"/> deliberately keeps its match private, which is right for the
/// shipped path and useless for a widget test that has to reach in and set a garrison to reproduce a
/// specific board. This exposes the match and nothing else; the snapshot it hands out is built by
/// the real <see cref="MatchSnapshotBuilder"/>, so what the widget under test reads is exactly what
/// it would read in play.
/// </summary>
internal sealed class TestMatchGateway : IMatchGateway
{
    private readonly MatchRunner _runner;

    public TestMatchGateway(Match match)
    {
        LiveMatch = match;
        _runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        CurrentSnapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
    }

    public Match LiveMatch { get; }

    public MatchSnapshot CurrentSnapshot { get; private set; }

    /// <summary>Re-reads the match - what the shipped gateway does on every tick and every accepted command.</summary>
    public void Refresh() => CurrentSnapshot = MatchSnapshotBuilder.Build(LiveMatch, LiveMatch.HumanPlayer);

    public void Advance(long elapsedMilliseconds)
    {
        _runner.Advance(elapsedMilliseconds / MW3.Core.Match.TickDurationMilliseconds);
        Refresh();
    }

    public GatewayCommandResult Submit(GatewayCommand command)
    {
        var accepted = command.Kind switch
        {
            GatewayCommandKind.Upgrade =>
                _runner.Execute(new UpgradeCommand(LiveMatch.HumanPlayer, command.FromBaseId)) == UpgradeOutcome.Accepted,
            GatewayCommandKind.Convert =>
                _runner.Execute(new ConvertCommand(LiveMatch.HumanPlayer, command.FromBaseId, command.TargetType!.Value)) == ConvertOutcome.Accepted,
            GatewayCommandKind.SendArmy =>
                _runner.Execute(new SendArmyCommand(
                    LiveMatch.HumanPlayer,
                    command.FromBaseId,
                    command.ToBaseId!.Value,
                    SendStrengthCalculator.Compute(GarrisonAt(command.FromBaseId), command.Strength!.Value))) == SendArmyOutcome.Accepted,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown gateway command kind."),
        };

        Refresh();
        return accepted ? GatewayCommandResult.Ok() : GatewayCommandResult.Rejected(command.Kind + " rejected");
    }

    public void Dispose()
    {
        // Nothing to release, exactly as the shipped loopback gateway has nothing to release.
    }

    private int GarrisonAt(int baseId) => LiveMatch.Bases.Single(b => b.Id == baseId).GarrisonCount;
}
