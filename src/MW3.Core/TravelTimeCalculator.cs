namespace MW3.Core;

/// <summary>
/// The travel-time arithmetic shared by <see cref="Match"/> (resolving a send) and
/// <see cref="AiBrain"/> (predicting one before committing to it) - kept in one place so the two
/// can never quietly disagree on how long an army takes to arrive.
/// </summary>
internal static class TravelTimeCalculator
{
    /// <summary>
    /// <paramref name="pathLength"/> is a route's total length (FR-3, <see cref="PathCalculator"/> -
    /// a straight-line distance only when nothing detours it) and <paramref name="speedUnitsPerTick"/>
    /// is the sender's effective speed (FR-4, <see cref="Match.EffectiveArmySpeedUnitsPerTick"/>) -
    /// both read once by the caller at submission and passed in, never recomputed here, so this stays
    /// a pure function of its inputs (D-39, D-53).
    /// </summary>
    internal static long ComputeTicks(double pathLength, double speedUnitsPerTick)
    {
        var ticks = (long)Math.Ceiling(pathLength / speedUnitsPerTick);
        return Math.Max(1, ticks);
    }
}
