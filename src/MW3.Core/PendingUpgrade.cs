namespace MW3.Core;

/// <summary>
/// An in-progress upgrade: the base stays at its current level until <see cref="PendingConstruction.CompletionTick"/>, then rises to <see cref="TargetLevel"/>.
/// </summary>
public sealed record PendingUpgrade(long CompletionTick, int TargetLevel) : PendingConstruction(CompletionTick);
