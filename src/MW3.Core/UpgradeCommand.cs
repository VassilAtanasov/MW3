namespace MW3.Core;

/// <summary>
/// Starts raising one owned base by one level, paid for out of that base's own garrison immediately
/// (D-22). The level itself rises only once <see cref="Match.Advance"/> reaches the build's
/// completion tick (D-30, FR-3c) - a build time is a delay on the benefit, not on the cost. Carries
/// no cost: what an upgrade costs is a rule, read from <see cref="LevelTable"/> when the command is
/// applied - a caller that named its own price could buy a level cheaply.
/// </summary>
public sealed record UpgradeCommand(Player IssuingPlayer, int BaseId);
