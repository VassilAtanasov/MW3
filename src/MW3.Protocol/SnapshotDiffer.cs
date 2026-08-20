namespace MW3.Protocol;

/// <summary>
/// Turns two <see cref="MatchSnapshot"/>s into an ordered <see cref="EventBatch"/> (D-58). A pure
/// function of the two snapshots - it never touches <c>Match</c>, so the events it produces cannot
/// disagree with the state they describe, and calling it twice on the same pair produces a
/// byte-identical batch.
///
/// Works on non-adjacent snapshots exactly as well as adjacent ones: nothing here assumes the two
/// ticks are one apart, because FR-4 may send below the simulation's own tick rate.
/// </summary>
public static class SnapshotDiffer
{
    /// <summary>
    /// Diffs <paramref name="a"/> against <paramref name="b"/>. Both must describe the same match
    /// (same map); diffing snapshots from two different matches throws rather than producing a
    /// nonsense batch.
    /// </summary>
    public static EventBatch Diff(MatchSnapshot a, MatchSnapshot b)
    {
        if (a is null)
        {
            throw new ArgumentNullException(nameof(a));
        }

        if (b is null)
        {
            throw new ArgumentNullException(nameof(b));
        }

        if (a.MapId != b.MapId)
        {
            throw new InvalidOperationException(
                $"Cannot diff snapshots from different maps: '{a.MapId ?? "(none)"}' and '{b.MapId ?? "(none)"}'.");
        }

        var events = new List<MatchEvent>();

        DiffBases(a, b, events);
        DiffArmies(a, b, events);
        DiffPlayers(a, b, events);
        DiffOutcome(a, b, events);

        return new EventBatch(a.ElapsedTicks, b.ElapsedTicks, events);
    }

    /// <summary>Bases ascending by id. The set of base ids never changes over a match's life, so every id in <paramref name="a"/> is also in <paramref name="b"/>.</summary>
    private static void DiffBases(MatchSnapshot a, MatchSnapshot b, List<MatchEvent> events)
    {
        var previousById = ToDictionary(a.Bases, x => x.Id);

        foreach (var newBase in b.Bases.OrderBy(x => x.Id))
        {
            if (!previousById.TryGetValue(newBase.Id, out var oldBase))
            {
                throw new InvalidOperationException($"Base {newBase.Id} appears in the later snapshot but not the earlier one - bases cannot be created mid-match.");
            }

            var kind = ClassifyBaseChange(oldBase, newBase);
            if (kind is { } k)
            {
                events.Add(new MatchEvent(k, newBase.Id, newBase, ArmyId: null, Army: null, LastKnownUnitCount: null, PlayerId: null, Player: null, Outcome: null));
            }
        }
    }

    /// <summary>
    /// Which kind of base event, if any, <paramref name="oldBase"/> to <paramref name="newBase"/> is.
    /// An owner change always wins (BaseCaptured, never also a plain BaseChanged for the same base in
    /// the same batch). A construction transition is next. Everything else that changed, other than
    /// the available-actions list alone, falls back to the generic BaseChanged.
    /// </summary>
    private static MatchEventKind? ClassifyBaseChange(BaseSnapshot oldBase, BaseSnapshot newBase)
    {
        if (oldBase.OwnerPlayerId != newBase.OwnerPlayerId)
        {
            return MatchEventKind.BaseCaptured;
        }

        var constructionChanged = oldBase.Construction != newBase.Construction;
        if (constructionChanged)
        {
            // A construction that both completed and was replaced by a new one within one
            // (possibly non-adjacent) diff looks the same as one that only started: both leave
            // Construction null-to-non-null across old and new, or non-null-to-non-null. The label
            // favours "started" because the final state has a pending construction of interest;
            // "completed" is used only when the final state has none. Either label is safe because
            // the event always carries the base's complete new state (D-70).
            return newBase.Construction is null ? MatchEventKind.ConstructionCompleted : MatchEventKind.ConstructionStarted;
        }

        // Compares every field except Construction (accounted for above) and AvailableActions
        // (accounted for below). OwnerPlayerId itself is excluded too - it is already known equal
        // at this point, the only way execution reaches here - but its two audit fields,
        // LastOwnerChangeTick and OwnerBeforeLastChangePlayerId, are NOT excluded: a base captured
        // and then recaptured back to its original owner within one (possibly non-adjacent) diff
        // window has an unchanged OwnerPlayerId but changed audit fields, and dropping that change
        // silently would leave SnapshotApplier reconstructing stale audit data - a real
        // apply(diff(a, b), a) == b failure the recapture grace (20 ticks) makes reachable well
        // within the gaps FR-2's own property test diffs.
        var coreEqual = oldBase.Position.Equals(newBase.Position)
            && oldBase.Type == newBase.Type
            && oldBase.Level == newBase.Level
            && oldBase.GarrisonCount == newBase.GarrisonCount
            && oldBase.GarrisonCap == newBase.GarrisonCap
            && oldBase.UpgradeCost == newBase.UpgradeCost
            && oldBase.DefencePercentage == newBase.DefencePercentage
            && oldBase.RingThicknessFractionOfRadius.Equals(newBase.RingThicknessFractionOfRadius)
            && oldBase.MaxLevel == newBase.MaxLevel
            && oldBase.MaxUpgradableLevel == newBase.MaxUpgradableLevel
            && oldBase.ProductionProgressTicks == newBase.ProductionProgressTicks
            && Nullable.Equals(oldBase.RangeUnits, newBase.RangeUnits)
            && oldBase.LastFireTick == newBase.LastFireTick
            && oldBase.LastOwnerChangeTick == newBase.LastOwnerChangeTick
            && oldBase.OwnerBeforeLastChangePlayerId == newBase.OwnerBeforeLastChangePlayerId;

        if (!coreEqual)
        {
            return MatchEventKind.BaseChanged;
        }

        return !SnapshotEquality.ListEquals(oldBase.AvailableActions, newBase.AvailableActions)
            ? MatchEventKind.AvailableActionsChanged
            : null;
    }

    /// <summary>
    /// Armies ascending by id. Unlike a base, an army can appear (launched) or disappear (removed)
    /// between the two ticks; every field but <see cref="ArmySnapshot.UnitCount"/> is fixed at
    /// launch (D-39, D-51), so an army present in both snapshots changes, if at all, only in count.
    /// </summary>
    private static void DiffArmies(MatchSnapshot a, MatchSnapshot b, List<MatchEvent> events)
    {
        var previousById = ToDictionary(a.Armies, x => x.Id);
        var laterById = ToDictionary(b.Armies, x => x.Id);

        var allIds = previousById.Keys.Union(laterById.Keys).OrderBy(id => id);
        foreach (var id in allIds)
        {
            var hadBefore = previousById.TryGetValue(id, out var before);
            var hasAfter = laterById.TryGetValue(id, out var after);

            if (!hadBefore && hasAfter)
            {
                events.Add(new MatchEvent(MatchEventKind.ArmyLaunched, BaseId: null, Base: null, id, after, LastKnownUnitCount: null, PlayerId: null, Player: null, Outcome: null));
            }
            else if (hadBefore && !hasAfter)
            {
                events.Add(new MatchEvent(MatchEventKind.ArmyRemoved, BaseId: null, Base: null, id, Army: null, before!.UnitCount, PlayerId: null, Player: null, Outcome: null));
            }
            else if (hadBefore && hasAfter && before != after)
            {
                events.Add(new MatchEvent(MatchEventKind.ArmyChanged, BaseId: null, Base: null, id, after, LastKnownUnitCount: null, PlayerId: null, Player: null, Outcome: null));
            }
        }
    }

    /// <summary>
    /// Players ascending by id. Morale and forge are diffed as separate concerns on the same player:
    /// both can change in one window, and both are reported, because they are different aspects of
    /// the player rather than one entity in the "one event per entity" sense that governs bases.
    /// </summary>
    private static void DiffPlayers(MatchSnapshot a, MatchSnapshot b, List<MatchEvent> events)
    {
        var previousById = ToDictionary(a.Players, x => x.Id);

        foreach (var newPlayer in b.Players.OrderBy(x => x.Id))
        {
            if (!previousById.TryGetValue(newPlayer.Id, out var oldPlayer))
            {
                throw new InvalidOperationException($"Player {newPlayer.Id} appears in the later snapshot but not the earlier one - players cannot be created mid-match.");
            }

            var moraleChanged = oldPlayer.MoralePoints != newPlayer.MoralePoints
                || oldPlayer.MoraleLevel != newPlayer.MoraleLevel
                || oldPlayer.MoraleAttackPercentage != newPlayer.MoraleAttackPercentage
                || oldPlayer.MoraleDefencePercentage != newPlayer.MoraleDefencePercentage;
            if (moraleChanged)
            {
                events.Add(new MatchEvent(MatchEventKind.MoraleChanged, BaseId: null, Base: null, ArmyId: null, Army: null, LastKnownUnitCount: null, newPlayer.Id, newPlayer, Outcome: null));
            }

            var forgeChanged = oldPlayer.ForgeCount != newPlayer.ForgeCount
                || oldPlayer.ForgeAttackPercentage != newPlayer.ForgeAttackPercentage
                || oldPlayer.ForgeDefencePercentage != newPlayer.ForgeDefencePercentage;
            if (forgeChanged)
            {
                events.Add(new MatchEvent(MatchEventKind.ForgeCountChanged, BaseId: null, Base: null, ArmyId: null, Army: null, LastKnownUnitCount: null, newPlayer.Id, newPlayer, Outcome: null));
            }
        }
    }

    private static void DiffOutcome(MatchSnapshot a, MatchSnapshot b, List<MatchEvent> events)
    {
        if (a.Outcome != b.Outcome)
        {
            events.Add(new MatchEvent(MatchEventKind.MatchEnded, BaseId: null, Base: null, ArmyId: null, Army: null, LastKnownUnitCount: null, PlayerId: null, Player: null, b.Outcome));
        }
    }

    private static Dictionary<int, T> ToDictionary<T>(IReadOnlyList<T> items, Func<T, int> keySelector)
    {
        var result = new Dictionary<int, T>(items.Count);
        foreach (var item in items)
        {
            result[keySelector(item)] = item;
        }

        return result;
    }
}
