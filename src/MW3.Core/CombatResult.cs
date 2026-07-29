namespace MW3.Core;

/// <summary>
/// The outcome of one <see cref="CombatResolver.Resolve"/> call: whether the attacker took the
/// base, and the garrison left standing - the attacker's surviving strength if captured, or the
/// defender's remaining garrison if it held.
/// </summary>
public readonly record struct CombatResult(bool Captured, int RemainingGarrison);
