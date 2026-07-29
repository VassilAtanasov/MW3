namespace MW3.Core;

/// <summary>
/// Starts converting one owned base to <see cref="TargetType"/>, either direction, paid for out of
/// that base's own garrison immediately (D-22). The type itself does not change until
/// <see cref="Match.Advance"/> reaches the build's completion tick (D-30, FR-3c) - until then the
/// base keeps its previous type entirely. Carries no cost: what a conversion costs is a rule, read
/// from <see cref="LevelTable"/> when the command is applied. Carries the target type explicitly
/// rather than a toggle, so a stale command replayed twice does the same thing both times instead of
/// flipping back.
/// </summary>
public sealed record ConvertCommand(Player IssuingPlayer, int BaseId, BaseType TargetType);
