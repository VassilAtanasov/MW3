namespace MW3.Protocol;

/// <summary>
/// A base's construction in progress (D-30), flattened for the wire. The rules model this as a
/// <c>PendingUpgrade</c>/<c>PendingConversion</c> hierarchy so a caller pattern-matches on which it
/// has; JSON has no case for that, so the kind is named explicitly and exactly one of
/// <paramref name="TargetLevel"/> and <paramref name="TargetType"/> is set. Flattening here rather
/// than shipping a polymorphic payload keeps the serializer free of type discriminators, which is
/// what keeps the Android head's trimmed build honest.
/// </summary>
/// <param name="Kind">Upgrade or convert - which of the two targets below is the real one.</param>
/// <param name="CompletionTick">The tick the construction completes on.</param>
/// <param name="TargetLevel">The level being built to, null for a conversion.</param>
/// <param name="TargetType">The type being converted to, null for an upgrade.</param>
public sealed record PendingConstructionSnapshot(
    BaseActionKind Kind,
    long CompletionTick,
    int? TargetLevel,
    BaseType? TargetType);
