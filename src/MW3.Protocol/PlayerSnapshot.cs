namespace MW3.Protocol;

/// <summary>
/// One player as the wire sees them: their identity, and every global multiplier that player
/// currently plays under. The percentages are carried as values rather than as the inputs they are
/// derived from because after FR-3 the client has no <c>MoraleTable</c> or <c>ForgeTable</c> to
/// derive them with - and should not: a client that can compute a combat index is a client that can
/// disagree with the server about one.
/// </summary>
/// <param name="Id">This player's id, the value bases and armies name their owner by.</param>
/// <param name="ControllerKind">Whether a human or the AI is deciding for this player.</param>
/// <param name="MoralePoints">Morale points, 0..8000 (D-38).</param>
/// <param name="MoraleLevel">The 0-5 sun level <paramref name="MoralePoints"/> derives to.</param>
/// <param name="MoraleAttackPercentage">Attack index contribution from morale, 100 at level 0.</param>
/// <param name="MoraleDefencePercentage">Defence index contribution from morale, 100 at level 0.</param>
/// <param name="ForgeCount">Forges this player owns - uncapped, unlike the four that contribute.</param>
/// <param name="ForgeAttackPercentage">Attack index contribution from forges, capped at four (D-42).</param>
/// <param name="ForgeDefencePercentage">Defence index contribution from forges, capped at four (D-42).</param>
public sealed record PlayerSnapshot(
    int Id,
    PlayerControllerKind ControllerKind,
    int MoralePoints,
    int MoraleLevel,
    int MoraleAttackPercentage,
    int MoraleDefencePercentage,
    int ForgeCount,
    int ForgeAttackPercentage,
    int ForgeDefencePercentage);
