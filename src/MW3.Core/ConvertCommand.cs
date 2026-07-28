namespace MW3.Core;

/// <summary>
/// Converts one owned base to <see cref="TargetType"/>, either direction, paid for out of that
/// base's own garrison (D-22). Carries no cost: what a conversion costs is a rule, read from
/// <see cref="LevelTable"/> when the command is applied. Carries the target type explicitly rather
/// than a toggle, so a stale command replayed twice does the same thing both times instead of
/// flipping back.
/// </summary>
public sealed record ConvertCommand(Player IssuingPlayer, int BaseId, BaseType TargetType);
