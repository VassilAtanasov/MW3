namespace MW3.Core;

/// <summary>
/// A base's construction in progress (D-30): the tick it completes on, carried on <see cref="Base"/>
/// rather than in a separate queue, so <see cref="Match.Advance"/> completes it exactly as it already
/// resolves production and arrivals. What the base is becoming is modelled in the type through
/// <see cref="PendingUpgrade"/> and <see cref="PendingConversion"/> rather than as nullable
/// level/type fields both present on one record - a caller pattern-matches on which it has instead of
/// reading whichever field happens to apply.
/// </summary>
public abstract record PendingConstruction(long CompletionTick);
