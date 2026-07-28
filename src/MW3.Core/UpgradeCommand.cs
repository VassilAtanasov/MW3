namespace MW3.Core;

/// <summary>
/// Raises one owned base by one level, paid for out of that base's own garrison (D-22). Carries no
/// cost: what an upgrade costs is a rule, read from <see cref="LevelTable"/> when the command is
/// applied - a caller that named its own price could buy a level cheaply.
/// </summary>
public sealed record UpgradeCommand(Player IssuingPlayer, int BaseId);
