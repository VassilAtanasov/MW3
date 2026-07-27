namespace MW3.Core;

/// <summary>
/// An army in flight between two bases (D-12). Inert while travelling - nothing intercepts,
/// damages, redirects, or recalls it, and capturing its source base does not change its owner or
/// destination. Removed from <see cref="Match.ArmiesInFlight"/> in the same
/// <see cref="Match.Advance"/> call that resolves its arrival.
/// </summary>
public sealed record Army(int Id, Player Owner, int SourceBaseId, int TargetBaseId, int UnitCount, long LaunchTick, long ArrivalTick);
