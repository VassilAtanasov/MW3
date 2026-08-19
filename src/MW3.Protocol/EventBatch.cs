namespace MW3.Protocol;

/// <summary>
/// An ordered run of <see cref="MatchEvent"/>s between two ticks of one match. Named by its
/// endpoints rather than by a separate sequence counter, because a snapshot's elapsed ticks are
/// already monotonic and so already serve as the sequence (D-70). A receiver detects a gap with
/// <c>batch.FromTick == currentTick</c>; what to do about a gap is FR-4's policy, not this type's.
/// </summary>
/// <param name="FromTick">The elapsed ticks of the earlier snapshot this batch was diffed from.</param>
/// <param name="ToTick">The elapsed ticks of the later snapshot this batch, applied, reproduces.</param>
/// <param name="Events">
/// Every change between the two ticks, in canonical order: bases ascending by id, then armies
/// ascending by id, then match-level events (morale, forge count, outcome) ascending by player id
/// and ending with <see cref="MatchEventKind.MatchEnded"/> if present.
/// </param>
public sealed record EventBatch(long FromTick, long ToTick, IReadOnlyList<MatchEvent> Events)
{
    /// <summary>Every change between the two ticks, in canonical order (see the type's own doc comment).</summary>
    public IReadOnlyList<MatchEvent> Events { get; } = Events ?? throw new ArgumentNullException(nameof(Events));

    /// <inheritdoc />
    public bool Equals(EventBatch? other) =>
        other is not null
        && FromTick == other.FromTick
        && ToTick == other.ToTick
        && SnapshotEquality.ListEquals(Events, other.Events);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked((((FromTick.GetHashCode() * 31) + ToTick.GetHashCode()) * 31) + SnapshotEquality.ListHash(Events));
}
