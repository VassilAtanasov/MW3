namespace MW3.Protocol;

/// <summary>
/// One change between two snapshots, as a complete delta carrying a semantic label (D-70). Every
/// event carries every changed field of the entity it describes - for a base or army that means the
/// entire new <see cref="BaseSnapshot"/> or <see cref="ArmySnapshot"/>, so <c>apply</c> never has to
/// guess a field it was not told about. The <see cref="Kind"/> is derived from which fields changed,
/// never inferred in a way that would let one be dropped.
///
/// Only the fields relevant to <see cref="Kind"/> are non-null; which ones those are is documented on
/// each <see cref="MatchEventKind"/> member. This mirrors <see cref="PendingConstructionSnapshot"/>'s
/// flattening: one shape, a kind tag, and JSON has no case for a discriminated union.
/// </summary>
/// <param name="Kind">What changed and about what.</param>
/// <param name="BaseId">
/// The base this event is about, for every base-related kind (<see cref="MatchEventKind.BaseCaptured"/>,
/// <see cref="MatchEventKind.BaseChanged"/>, <see cref="MatchEventKind.ConstructionStarted"/>,
/// <see cref="MatchEventKind.ConstructionCompleted"/>, <see cref="MatchEventKind.AvailableActionsChanged"/>).
/// </param>
/// <param name="Base">The base's complete new state, for every base-related kind.</param>
/// <param name="ArmyId">The army this event is about, for every army-related kind.</param>
/// <param name="Army">
/// The army's complete new state, for <see cref="MatchEventKind.ArmyLaunched"/> and
/// <see cref="MatchEventKind.ArmyChanged"/>. Null for <see cref="MatchEventKind.ArmyRemoved"/>, which
/// carries <see cref="LastKnownUnitCount"/> instead.
/// </param>
/// <param name="LastKnownUnitCount">
/// The army's strength as of the earlier snapshot, carried only by <see cref="MatchEventKind.ArmyRemoved"/>.
/// Deliberately not a reason: whether the army arrived or was destroyed in flight cannot be told apart
/// from the two snapshots alone (an army whose strength reaches zero on exactly its arrival tick looks
/// identical either way), so none is claimed.
/// </param>
/// <param name="PlayerId">The player this event is about, for <see cref="MatchEventKind.MoraleChanged"/> and <see cref="MatchEventKind.ForgeCountChanged"/>.</param>
/// <param name="Player">The player's complete new state, for <see cref="MatchEventKind.MoraleChanged"/> and <see cref="MatchEventKind.ForgeCountChanged"/>.</param>
/// <param name="Outcome">The match's new outcome, for <see cref="MatchEventKind.MatchEnded"/> only.</param>
public sealed record MatchEvent(
    MatchEventKind Kind,
    int? BaseId,
    BaseSnapshot? Base,
    int? ArmyId,
    ArmySnapshot? Army,
    int? LastKnownUnitCount,
    int? PlayerId,
    PlayerSnapshot? Player,
    MatchOutcome? Outcome);
