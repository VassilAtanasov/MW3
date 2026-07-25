namespace MW3.Core;

/// <summary>
/// Deterministic fixed-step tick accumulator. Advances by an elapsed millisecond count and
/// reports how many whole ticks have passed, carrying any remainder to the next call. Has no
/// wall-clock or platform dependency, so callers supply elapsed time explicitly.
/// </summary>
public readonly struct FixedStepClock
{
    public FixedStepClock(long tickDurationMilliseconds, long carryOverMilliseconds = 0)
    {
        if (tickDurationMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickDurationMilliseconds), tickDurationMilliseconds, "Tick duration must be positive.");
        }

        if (carryOverMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(carryOverMilliseconds), carryOverMilliseconds, "Carry-over cannot be negative.");
        }

        TickDurationMilliseconds = tickDurationMilliseconds;
        CarryOverMilliseconds = carryOverMilliseconds;
    }

    public long TickDurationMilliseconds { get; }

    public long CarryOverMilliseconds { get; }

    /// <summary>
    /// Advances the clock by <paramref name="elapsedMilliseconds"/> and returns the resulting
    /// clock state together with the number of whole ticks that passed.
    /// </summary>
    public (FixedStepClock Clock, long Ticks) Advance(long elapsedMilliseconds)
    {
        if (TickDurationMilliseconds <= 0)
        {
            throw new InvalidOperationException("FixedStepClock must be constructed with a positive tick duration before calling Advance; the default(FixedStepClock) value is not usable.");
        }

        if (elapsedMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds), elapsedMilliseconds, "Elapsed time cannot be negative.");
        }

        var totalMilliseconds = CarryOverMilliseconds + elapsedMilliseconds;
        var ticks = totalMilliseconds / TickDurationMilliseconds;
        var carryOverMilliseconds = totalMilliseconds % TickDurationMilliseconds;
        return (new FixedStepClock(TickDurationMilliseconds, carryOverMilliseconds), ticks);
    }
}
