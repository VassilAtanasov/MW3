namespace MW3.Protocol;

/// <summary>
/// Reconstructs a later <see cref="MatchSnapshot"/> from an earlier one plus the <see cref="EventBatch"/>
/// <see cref="SnapshotDiffer.Diff"/> produced between them (D-58). Mutates neither argument; every
/// mutable collection is copied before being changed.
/// </summary>
public static class SnapshotApplier
{
    /// <summary>
    /// Applies <paramref name="batch"/> to <paramref name="snapshot"/>, returning a new snapshot at
    /// <see cref="EventBatch.ToTick"/>. Throws if the batch does not start where the snapshot leaves
    /// off - a silently misapplied batch is exactly the failure mode this design exists to prevent.
    /// </summary>
    public static MatchSnapshot Apply(EventBatch batch, MatchSnapshot snapshot)
    {
        if (batch is null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (batch.FromTick != snapshot.ElapsedTicks)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant($"Cannot apply a batch starting at tick {batch.FromTick} to a snapshot at tick {snapshot.ElapsedTicks}."));
        }

        var bases = new SortedDictionary<int, BaseSnapshot>();
        foreach (var b in snapshot.Bases)
        {
            bases[b.Id] = b;
        }

        // Not a SortedDictionary by id: an army's position in a match's own army list is its
        // insertion order, which is the tick it was promoted into flight - its LaunchTick - not its
        // id. A later send's wave 1 can enter flight before an earlier send's wave 3 does, so id
        // order and list order are different things. Reordering by (LaunchTick, Id) after every
        // event is applied reconstructs the true order regardless of the order events happened to
        // arrive in.
        var armies = new List<ArmySnapshot>(snapshot.Armies);

        var players = new SortedDictionary<int, PlayerSnapshot>();
        foreach (var p in snapshot.Players)
        {
            players[p.Id] = p;
        }

        var outcome = snapshot.Outcome;

        foreach (var e in batch.Events)
        {
            switch (e.Kind)
            {
                case MatchEventKind.BaseCaptured:
                case MatchEventKind.BaseChanged:
                case MatchEventKind.ConstructionStarted:
                case MatchEventKind.ConstructionCompleted:
                case MatchEventKind.AvailableActionsChanged:
                    bases[e.BaseId ?? throw MissingField(e.Kind, nameof(e.BaseId))] =
                        e.Base ?? throw MissingField(e.Kind, nameof(e.Base));
                    break;

                case MatchEventKind.ArmyLaunched:
                    armies.Add(e.Army ?? throw MissingField(e.Kind, nameof(e.Army)));
                    break;

                case MatchEventKind.ArmyChanged:
                    {
                        var id = e.ArmyId ?? throw MissingField(e.Kind, nameof(e.ArmyId));
                        var index = armies.FindIndex(x => x.Id == id);
                        armies[index] = e.Army ?? throw MissingField(e.Kind, nameof(e.Army));
                        break;
                    }

                case MatchEventKind.ArmyRemoved:
                    {
                        var id = e.ArmyId ?? throw MissingField(e.Kind, nameof(e.ArmyId));
                        armies.RemoveAll(x => x.Id == id);
                        break;
                    }

                case MatchEventKind.MoraleChanged:
                case MatchEventKind.ForgeCountChanged:
                    players[e.PlayerId ?? throw MissingField(e.Kind, nameof(e.PlayerId))] =
                        e.Player ?? throw MissingField(e.Kind, nameof(e.Player));
                    break;

                case MatchEventKind.MatchEnded:
                    outcome = e.Outcome ?? throw MissingField(e.Kind, nameof(e.Outcome));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(batch), e.Kind, "Unknown event kind.");
            }
        }

        armies.Sort((x, y) => x.LaunchTick != y.LaunchTick ? x.LaunchTick.CompareTo(y.LaunchTick) : x.Id.CompareTo(y.Id));

        return new MatchSnapshot(
            snapshot.ProtocolVersion,
            snapshot.MapId,
            batch.ToTick,
            outcome,
            snapshot.LocalPlayerId,
            snapshot.Obstacles,
            players.Values.ToList(),
            bases.Values.ToList(),
            armies);
    }

    private static InvalidOperationException MissingField(MatchEventKind kind, string field) =>
        new($"A {kind} event is missing its {field}.");
}
