namespace MW3.Core;

/// <summary>
/// The travel-time arithmetic shared by <see cref="Match"/> (resolving a send) and
/// <see cref="AiBrain"/> (predicting one before committing to it) - kept in one place so the two
/// can never quietly disagree on how long an army takes to arrive.
/// </summary>
internal static class TravelTimeCalculator
{
    internal static long ComputeTicks(MapPoint from, MapPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var ticks = (long)Math.Ceiling(distance / Match.ArmySpeedUnitsPerTick);
        return Math.Max(1, ticks);
    }
}
