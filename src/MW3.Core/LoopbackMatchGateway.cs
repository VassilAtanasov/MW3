namespace MW3.Core;

/// <summary>
/// An <see cref="IMatchGateway"/> that runs the match right here, in this process (D-74). It owns
/// one <see cref="Match"/>, one <see cref="MatchRunner"/> with a fresh <see cref="AiBrain"/>, and the
/// <see cref="FixedStepClock"/> that turns elapsed wall-clock milliseconds into whole ticks - all of
/// which <c>MatchScreen</c> owned before this feature. One instance per match: constructing this is
/// what starts a new match against a new opponent.
///
/// It lives in <c>MW3.Core</c> because it needs the rules, and the client cannot see the rules
/// (D-57). That is the whole reason the heads become the composition root.
///
/// <para>
/// The exposed snapshot is reached by <b>diffing and applying</b>, never by handing out the freshly
/// built one (D-61). That looks like pure ceremony from in here - the built snapshot is exactly what
/// apply reconstructs - and it is the point: every frame of every run of the game, and therefore
/// every committed <c>qa/scripts/</c> run, exercises the same pipeline the wire will use, including
/// FR-2's non-adjacent-snapshot case whenever more than one tick elapsed in a frame. Local play is
/// not a shortcut around the protocol.
/// </para>
/// </summary>
public sealed class LoopbackMatchGateway : IMatchGateway
{
    private readonly Match _match;
    private readonly MatchRunner _runner;

    private FixedStepClock _clock = new(Match.TickDurationMilliseconds);

    /// <summary>Starts a match on <paramref name="definition"/> against a fresh AI opponent.</summary>
    public LoopbackMatchGateway(MapDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        _match = new Match(definition);
        _runner = new MatchRunner(_match, new AiBrain(_match.AiPlayer));

        // The one snapshot that is handed out without being applied, because at tick 0 there is
        // nothing yet to diff it against. Every refresh after this goes through diff and apply.
        CurrentSnapshot = Build();
    }

    /// <inheritdoc />
    public MatchSnapshot CurrentSnapshot { get; private set; }

    /// <inheritdoc />
    public void Advance(long elapsedMilliseconds)
    {
        var (clock, ticks) = _clock.Advance(elapsedMilliseconds);
        _clock = clock;

        // Zero whole ticks is an ordinary frame with nothing to diff - the match did not move, so
        // the exposed snapshot cannot have changed. Skipping the refresh is what makes the diff
        // happen once per frame across however many ticks elapsed, rather than once per tick.
        if (ticks <= 0)
        {
            return;
        }

        _runner.Advance(ticks);
        Refresh();
    }

    /// <inheritdoc />
    public GatewayCommandResult Submit(GatewayCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var result = command.Kind switch
        {
            GatewayCommandKind.SendArmy => SubmitSendArmy(command),
            GatewayCommandKind.Upgrade => Describe(_runner.Execute(new UpgradeCommand(_match.HumanPlayer, command.FromBaseId)), UpgradeOutcome.Accepted),
            GatewayCommandKind.Convert => Describe(
                _runner.Execute(new ConvertCommand(_match.HumanPlayer, command.FromBaseId, command.TargetType!.Value)), // a Convert command validates its target type at construction
                ConvertOutcome.Accepted),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown gateway command kind."),
        };

        if (result.Accepted)
        {
            // A submitted command's effect has to be visible in the exposed snapshot immediately,
            // not only after the next Advance - the client draws again before it advances again.
            Refresh();
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: a match, its runner and its brain are plain managed objects holding no
        // unmanaged resource and no shared or static state, so a gateway going away takes its whole
        // match with it. The method exists so a client that pops a match screen and pushes another
        // has a place to say so, and so FR-4's implementation - which will own a live connection -
        // can be dropped in without the client learning a new lifecycle.
    }

    private GatewayCommandResult SubmitSendArmy(GatewayCommand command)
    {
        var source = FindBase(command.FromBaseId);
        if (source is null)
        {
            return GatewayCommandResult.Rejected(SendArmyOutcome.BaseNotFound.ToString());
        }

        // Resolved here, from the live garrison at the tick the command applies - never by the
        // client, which carries a SendStrength and no count (D-76).
        var unitCount = SendStrengthCalculator.Compute(source.GarrisonCount, command.Strength!.Value); // a SendArmy command validates its strength at construction

        return Describe(
            _runner.Execute(new SendArmyCommand(_match.HumanPlayer, command.FromBaseId, command.ToBaseId!.Value, unitCount)), // likewise its target base
            SendArmyOutcome.Accepted);
    }

    private static GatewayCommandResult Describe<TOutcome>(TOutcome outcome, TOutcome accepted)
        where TOutcome : struct, Enum =>
        EqualityComparer<TOutcome>.Default.Equals(outcome, accepted)
            ? GatewayCommandResult.Ok()
            : GatewayCommandResult.Rejected(outcome.ToString()!); // an enum's ToString is never null

    /// <summary>
    /// D-61: build, diff against what was last exposed, and reach the new exposed snapshot by
    /// applying that batch. Assigning <see cref="Build"/>'s result directly would be the same value
    /// and would quietly retire the only thing that keeps the diff/apply pair honest in real play.
    /// </summary>
    private void Refresh()
    {
        var previous = CurrentSnapshot;
        var built = Build();
        var batch = SnapshotDiffer.Diff(previous, built);
        CurrentSnapshot = SnapshotApplier.Apply(batch, previous);
    }

    private MatchSnapshot Build() => MatchSnapshotBuilder.Build(_match, _match.HumanPlayer);

    private Base? FindBase(int id)
    {
        var bases = _match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == id)
            {
                return bases[i];
            }
        }

        return null;
    }
}
