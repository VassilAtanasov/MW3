namespace MW3.Core;

/// <summary>
/// An in-progress conversion: the base stays its current <see cref="BaseType"/> until
/// <see cref="PendingConstruction.CompletionTick"/>, then becomes <see cref="TargetType"/> at the
/// minimum level.
/// </summary>
public sealed record PendingConversion(long CompletionTick, BaseType TargetType) : PendingConstruction(CompletionTick);
