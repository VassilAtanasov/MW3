namespace MW3.Core.Tests;

/// <summary>
/// The property FR-2 exists to prove: <c>apply(diff(a, b), a) == b</c> for every pair of snapshots a
/// real match can produce - not a handful of examples, and not only adjacent ticks (FR-4 may send
/// below the tick rate). Runs complete matches, both players AI-driven, on each of the three maps to
/// a decided outcome, snapshotting every tick, and diffing every adjacent pair plus a spread of
/// non-adjacent gaps.
/// </summary>
public class SnapshotDiffApplyPropertyTests
{
    private const long _maxTicks = 8000;

    private readonly record struct MapRun(MapDefinition Map, List<MatchSnapshot> History, List<EventBatch> AdjacentBatches);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void Execute(Match match, BrainDecision decision)
    {
        if (!decision.HasCommand)
        {
            return;
        }

        if (decision.IsUpgrade)
        {
            match.Execute(decision.Upgrade);
        }
        else if (decision.IsConvert)
        {
            match.Execute(decision.Convert);
        }
        else
        {
            match.Execute(decision.Command);
        }
    }

    /// <summary>
    /// Plays <paramref name="map"/> to a decided outcome with both players driven by
    /// <see cref="AiBrain"/>, capturing a snapshot (from the human's point of view) at every tick.
    /// </summary>
    private static List<MatchSnapshot> PlayToDecidedOutcome(MapDefinition map)
    {
        var match = new Match(map);
        var humanBrain = new AiBrain(match.HumanPlayer);
        var aiBrain = new AiBrain(match.AiPlayer);

        // Two identical AiBrain instances on Big's perfectly mirrored layout (a neutral forge
        // flanked symmetrically by two neutral towers) settle into a genuine standoff - verified by
        // running it unmodified for 400,000 ticks without a decision. Small and Medium are not
        // symmetric in the same way (their neutrals are not mirrored around both players equally)
        // and resolve organically; Big needs its tie broken to reach the decided outcome this test
        // requires, the same way AiTowerRoutingDeterminismTests rigs a starting garrison via
        // reflection to force a scenario rather than hope for one.
        if (map.Id == MapId.Big)
        {
            SetGarrison(match.Bases.Single(b => b.Owner == match.HumanPlayer), 400);
        }

        var history = new List<MatchSnapshot> { MatchSnapshotBuilder.Build(match, match.HumanPlayer) };

        while (match.Outcome == MatchOutcome.InProgress && match.ElapsedTicks < _maxTicks)
        {
            if (match.ElapsedTicks % MatchRunner.DecisionIntervalTicks == 0)
            {
                Execute(match, humanBrain.Decide(match));
                Execute(match, aiBrain.Decide(match));
            }

            match.Advance(1);
            history.Add(MatchSnapshotBuilder.Build(match, match.HumanPlayer));
        }

        Assert.NotEqual(MatchOutcome.InProgress, match.Outcome);
        return history;
    }

    private static void AssertRoundTrips(MatchSnapshot a, MatchSnapshot b)
    {
        var batch = SnapshotDiffer.Diff(a, b);
        Assert.Equal(a.ElapsedTicks, batch.FromTick);
        Assert.Equal(b.ElapsedTicks, batch.ToTick);

        var reconstructed = SnapshotApplier.Apply(batch, a);
        Assert.Equal(b, reconstructed);
    }

    private static MapRun Play(MapDefinition map)
    {
        var history = PlayToDecidedOutcome(map);
        var adjacentBatches = new List<EventBatch>(history.Count - 1);

        for (var i = 0; i < history.Count - 1; i++)
        {
            AssertRoundTrips(history[i], history[i + 1]);
            adjacentBatches.Add(SnapshotDiffer.Diff(history[i], history[i + 1]));
        }

        foreach (var gap in new[] { 2, 5, 20, 100 })
        {
            for (var i = 0; i + gap < history.Count; i += Math.Max(1, gap * 3))
            {
                AssertRoundTrips(history[i], history[i + gap]);
            }
        }

        AssertRoundTrips(history[0], history[^1]);

        return new MapRun(map, history, adjacentBatches);
    }

    private static bool HasMultiWaveSend(MapRun run) =>
        run.History.Any(s => s.Armies.Any(a => a.WaveCount > 1));

    private static bool HasDetouredPath(MapRun run) =>
        run.History.Any(s => s.Armies.Any(a => a.PathWaypoints.Count > 2));

    private static bool HasCapture(MapRun run) =>
        run.AdjacentBatches.Any(batch => batch.Events.Any(e => e.Kind == MatchEventKind.BaseCaptured));

    private static bool HasConstructionStartedAndCompleted(MapRun run) =>
        run.AdjacentBatches.Any(batch => batch.Events.Any(e => e.Kind == MatchEventKind.ConstructionStarted))
        && run.AdjacentBatches.Any(batch => batch.Events.Any(e => e.Kind == MatchEventKind.ConstructionCompleted));

    private static bool HasTowerFiring(MapRun run) =>
        run.History.Any(s => s.Bases.Any(b => b.LastFireTick is not null));

    private static bool HasMoraleLevelChange(MapRun run) =>
        run.AdjacentBatches.Any(batch => batch.Events.Any(e => e.Kind == MatchEventKind.MoraleChanged));

    private static bool HasForgeCountChange(MapRun run) =>
        run.AdjacentBatches.Any(batch => batch.Events.Any(e => e.Kind == MatchEventKind.ForgeCountChanged));

    /// <summary>
    /// One test rather than three, so each map is only ever played once: playing to a decided
    /// outcome and diffing every adjacent tick is the expensive part, and the event-coverage
    /// assertion below reuses the batches this already computed instead of re-deriving them.
    ///
    /// Coverage is asserted across the union of all three maps' histories - only
    /// <see cref="MapCatalog.Big"/> ships a neutral tower and a neutral forge from the start (phase 6
    /// FR-2), so tower fire and a forge count change are only guaranteed there. A suite that "passed"
    /// on a match where nothing interesting happened would be worthless, which is what this guards
    /// against.
    /// </summary>
    [Fact]
    public void ApplyOfDiff_ReproducesTheLaterSnapshot_AndTheHistoriesTogetherCoverEveryInterestingEvent()
    {
        var runs = new[] { Play(MapCatalog.Small), Play(MapCatalog.Medium), Play(MapCatalog.Big) };

        Assert.True(runs.Any(HasMultiWaveSend), "No history contained a multi-wave send.");
        Assert.True(runs.Any(HasDetouredPath), "No history contained a detoured path.");
        Assert.True(runs.Any(HasCapture), "No history contained a base capture.");
        Assert.True(runs.Any(HasConstructionStartedAndCompleted), "No history contained both a construction start and its completion.");
        Assert.True(runs.Any(HasTowerFiring), "No history contained a tower firing.");
        Assert.True(runs.Any(HasMoraleLevelChange), "No history contained a morale level change.");
        Assert.True(runs.Any(HasForgeCountChange), "No history contained a forge count change.");
    }
}
