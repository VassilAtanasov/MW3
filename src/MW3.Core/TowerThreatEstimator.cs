namespace MW3.Core;

/// <summary>
/// Pure geometry (FR-7, phase 7 FR-6): estimates how many units an enemy tower would remove from an
/// army in transit, without simulating the match. Used by <see cref="AiBrain"/> to weigh an attack
/// before committing to it. The estimate walks the <see cref="ArmyPath"/> polyline the army was
/// given at submission (D-51) - the same route <see cref="PathCalculator"/> hands the real send -
/// so the AI costs tower fire along the segments its army actually crosses, never along a straight
/// line it never flies.
/// </summary>
internal static class TowerThreatEstimator
{
    /// <summary>
    /// The total chord of <paramref name="path"/> that falls within <paramref name="towerLevel"/>'s
    /// range circle around <paramref name="towerPosition"/> - summed over every consecutive waypoint
    /// pair - converted to ticks via <paramref name="speedUnitsPerTick"/> (FR-4, the crossing army's
    /// own effective speed - a faster army spends fewer ticks in range) and divided by the tower's
    /// <see cref="LevelTable.Tower.FirePeriodTicks(int)"/> at that level, floored - one unit lost per
    /// shot, mirroring <c>Match.EvaluateTowerFireAtTick</c>'s own damage model so the estimate and
    /// the simulation agree in kind. Zero when no segment ever enters range.
    /// <para>
    /// The chords are summed <b>before</b> the conversion and the result is floored exactly
    /// <b>once</b>, never per segment: flooring each segment would discard every segment's
    /// fractional tail and systematically understate a detour's threat, which is precisely the
    /// continuous model <c>Match.EvaluateTowerFireAtTick</c> does not use. A tower in range across a
    /// waypoint join therefore scores the same as one in range of a single straight segment of the
    /// same total length.
    /// </para>
    /// </summary>
    internal static int EstimateUnitsLost(ArmyPath path, MapPoint towerPosition, int towerLevel, double speedUnitsPerTick)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var range = LevelTable.Tower.RangeUnits(towerLevel);
        var waypoints = path.Waypoints;

        var chordLength = 0.0;
        for (var i = 1; i < waypoints.Count; i++)
        {
            chordLength += ChordLengthWithinRange(waypoints[i - 1], waypoints[i], towerPosition, range);
        }

        if (chordLength <= 0.0)
        {
            return 0;
        }

        var ticksInRange = chordLength / speedUnitsPerTick;
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
