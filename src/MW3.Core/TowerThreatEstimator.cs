namespace MW3.Core;

/// <summary>
/// Pure geometry (FR-7): estimates how many units an enemy tower would remove from an army flying a
/// straight segment, without simulating the match. Used by <see cref="AiBrain"/> to weigh an attack
/// before committing to it - the map has no pathfinding (REQUIREMENTS.md §6), so "routing around" a
/// tower means preferring a different source/target pair, not a different path between the same two
/// points.
/// </summary>
internal static class TowerThreatEstimator
{
    /// <summary>
    /// The chord of <paramref name="from"/>-<paramref name="to"/> that falls within
    /// <paramref name="towerLevel"/>'s range circle around <paramref name="towerPosition"/>,
    /// converted to ticks via <see cref="Match.ArmySpeedUnitsPerTick"/> and divided by the tower's
    /// <see cref="LevelTable.Tower.FirePeriodTicks(int)"/> at that level, floored - one unit lost per
    /// shot, mirroring <c>Match.EvaluateTowerFireAtTick</c>'s own damage model (FR-4) so the estimate
    /// and the simulation agree in kind. Zero when the segment never enters range, including a
    /// zero-length segment exactly on the boundary.
    /// </summary>
    internal static int EstimateUnitsLost(MapPoint from, MapPoint to, MapPoint towerPosition, int towerLevel)
    {
        var chordLength = ChordLengthWithinRange(from, to, towerPosition, LevelTable.Tower.RangeUnits(towerLevel));
        if (chordLength <= 0.0)
        {
            return 0;
        }

        var ticksInRange = chordLength / Match.ArmySpeedUnitsPerTick;
        var firePeriod = LevelTable.Tower.FirePeriodTicks(towerLevel);
        return (int)Math.Floor(ticksInRange / firePeriod);
    }

    /// <summary>
    /// The length of the portion of segment <paramref name="from"/>-<paramref name="to"/> that lies
    /// within <paramref name="range"/> of <paramref name="center"/>: the standard line/circle
    /// intersection, parameterized as <c>from + t * (to - from)</c> for <c>t</c> in <c>[0, 1]</c> and
    /// solved for the interval of <c>t</c> inside the circle, clamped to the segment itself. Zero if
    /// the segment never enters the circle, is tangent to it, or has zero length.
    /// </summary>
    private static double ChordLengthWithinRange(MapPoint from, MapPoint to, MapPoint center, double range)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var segmentLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (segmentLength <= 0.0)
        {
            return 0.0;
        }

        var fx = from.X - center.X;
        var fy = from.Y - center.Y;

        var a = (dx * dx) + (dy * dy);
        var b = 2 * ((dx * fx) + (dy * fy));
        var c = (fx * fx) + (fy * fy) - (range * range);

        var discriminant = (b * b) - (4 * a * c);
        if (discriminant <= 0.0)
        {
            return 0.0;
        }

        var sqrtDiscriminant = Math.Sqrt(discriminant);
        var t1 = (-b - sqrtDiscriminant) / (2 * a);
        var t2 = (-b + sqrtDiscriminant) / (2 * a);

        var clampedT1 = Math.Clamp(t1, 0.0, 1.0);
        var clampedT2 = Math.Clamp(t2, 0.0, 1.0);
        if (clampedT2 <= clampedT1)
        {
            return 0.0;
        }

        return (clampedT2 - clampedT1) * segmentLength;
    }
}
