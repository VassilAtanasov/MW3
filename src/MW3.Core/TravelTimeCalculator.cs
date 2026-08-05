namespace MW3.Core;

/// <summary>
/// The travel-time arithmetic shared by <see cref="Match"/> (resolving a send) and
/// <see cref="AiBrain"/> (predicting one before committing to it) - kept in one place so the two
/// can never quietly disagree on how long an army takes to arrive.
/// </summary>
internal static class TravelTimeCalculator
{
    /// <summary>
    /// <paramref name="speedUnitsPerTick"/> is the sender's effective speed (FR-4,
    /// <see cref="Match.EffectiveArmySpeedUnitsPerTick"/>) - read once by the caller at submission
    /// and passed in, never recomputed here, so this stays a pure function of its inputs (D-39).
    /// </summary>
    internal static long ComputeTicks(MapPoint from, MapPoint to, double speedUnitsPerTick)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var ticks = (long)Math.Ceiling(distance / speedUnitsPerTick);
        return Math.Max(1, ticks);
    }
}
