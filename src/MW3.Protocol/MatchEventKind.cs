namespace MW3.Protocol;

/// <summary>
/// What kind of change a <see cref="MatchEvent"/> describes, derived from *which* fields changed
/// between two snapshots (D-70) - never inferred in a way that would let a field be omitted.
/// </summary>
public enum MatchEventKind
{
    /// <summary>A base's owner changed, including to or from neutral. Excludes a plain <see cref="BaseChanged"/> for the same base in the same batch.</summary>
    BaseCaptured,

    /// <summary>Any other change to a base: level, garrison, defence, production progress, or the tower-fire tick.</summary>
    BaseChanged,

    /// <summary>A base began building - an upgrade or a conversion.</summary>
    ConstructionStarted,

    /// <summary>A base's pending construction finished.</summary>
    ConstructionCompleted,

    /// <summary>An army new to the snapshot - it launched between the two ticks diffed.</summary>
    ArmyLaunched,

    /// <summary>An army already known whose strength changed (tower fire).</summary>
    ArmyChanged,

    /// <summary>
    /// An army present in the earlier snapshot and absent from the later one. Carries no
    /// arrived-vs-destroyed reason (see <see cref="MatchEvent.LastKnownUnitCount"/>).
    /// </summary>
    ArmyRemoved,

    /// <summary>A player's morale points, level, or morale-derived percentages changed.</summary>
    MoraleChanged,

    /// <summary>A player's forge count or forge-derived percentages changed.</summary>
    ForgeCountChanged,

    /// <summary>A base's available actions changed with nothing else about it changing.</summary>
    AvailableActionsChanged,

    /// <summary>The match's outcome changed from undecided to decided.</summary>
    MatchEnded,
}
