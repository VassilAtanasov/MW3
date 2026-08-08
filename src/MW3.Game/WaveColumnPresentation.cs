using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Pure geometry and timing arithmetic for FR-4's wave-column legibility (D-36) - decides nothing
/// and draws nothing, mirroring how <see cref="SendStrengthSelector"/> keeps its layout math
/// headlessly testable (D-25). <see cref="MatchScreen"/> owns every MonoGame type and every reused
/// buffer; this class only ever sees plain data.
/// </summary>
internal static class WaveColumnPresentation
{
    /// <summary>
    /// Wave <paramref name="waveIndex"/> (1-based) of <paramref name="waveCount"/>'s drawn radius,
    /// as a fraction of the viewport's smaller dimension - <paramref name="leadRadiusFraction"/> at
    /// wave 1, <paramref name="trailingRadiusFraction"/> at the last wave, linear in between. A
    /// single-wave send (<paramref name="waveCount"/> &lt;= 1) always returns
    /// <paramref name="leadRadiusFraction"/> unchanged, so an ordinary send draws bit-identically to
    /// before this feature.
    /// </summary>
    public static float RadiusFraction(int waveIndex, int waveCount, float leadRadiusFraction, float trailingRadiusFraction)
    {
        if (waveCount <= 1)
        {
            return leadRadiusFraction;
        }

        var t = (float)(waveIndex - 1) / (waveCount - 1);
        return leadRadiusFraction + ((trailingRadiusFraction - leadRadiusFraction) * t);
    }

    /// <summary>
    /// Fills <paramref name="output"/> with one (FromIndex, ToIndex) pair per spine segment - the
    /// indices into <paramref name="armiesInFlight"/> of two consecutive, currently-in-flight waves
    /// of the same send (grouped by <see cref="Army.SendId"/>, never by adjacency in the list, since
    /// a second send launched mid-column interleaves with the first). A send whose lead wave has
    /// already arrived and whose later waves are still pending draws a spine across only what is
    /// actually in <paramref name="armiesInFlight"/>, because a pending wave is simply absent from
    /// that list until its own launch tick (FR-3). Clears <paramref name="output"/> first and never
    /// allocates - callers reuse the same list every call, matching the no-per-frame-allocation rule
    /// (docs/CONVENTIONS.md) since this runs from <see cref="MatchScreen.Draw"/>.
    /// </summary>
    public static void ComputeSpineSegments(IReadOnlyList<Army> armiesInFlight, List<(int FromIndex, int ToIndex)> output)
    {
        ArgumentNullException.ThrowIfNull(armiesInFlight);
        ArgumentNullException.ThrowIfNull(output);

        output.Clear();

        for (var i = 0; i < armiesInFlight.Count; i++)
        {
            var current = armiesInFlight[i];
            var nextIndex = -1;
            var nextWaveIndex = int.MaxValue;

            for (var j = 0; j < armiesInFlight.Count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                var candidate = armiesInFlight[j];
                if (candidate.SendId != current.SendId || candidate.WaveIndex <= current.WaveIndex)
                {
                    continue;
                }

                if (candidate.WaveIndex < nextWaveIndex)
                {
                    nextWaveIndex = candidate.WaveIndex;
                    nextIndex = j;
                }
            }

            if (nextIndex >= 0)
            {
                output.Add((i, nextIndex));
            }
        }
    }

    /// <summary>
    /// Whether a presentation event (a tower's <see cref="Base.LastFireTick"/>, or the tick an
    /// in-flight army's <see cref="Army.UnitCount"/> was last observed to drop) should still read as
    /// a flash at <paramref name="elapsedTicks"/> - true while the event is within
    /// <paramref name="durationTicks"/> ticks in the past, false if it never happened
    /// (<paramref name="eventTick"/> is null) or has aged out.
    /// </summary>
    public static bool IsFlashing(long elapsedTicks, long? eventTick, int durationTicks) =>
        eventTick is long tick && elapsedTicks - tick < durationTicks;

    /// <summary>
    /// Fills <paramref name="output"/> with the point run a spine segment between two waves of one
    /// send should draw (FR-4): <paramref name="fromProgress"/>'s point on <paramref name="path"/>,
    /// then every waypoint strictly between the two fractions' arc-length distances (in path order -
    /// the order <see cref="ArmyPath.Waypoints"/> already carries, source to target), then
    /// <paramref name="toProgress"/>'s point - always at least two points, more where the two
    /// fractions straddle a corner. A caller following <see cref="ComputeSpineSegments"/>'s own
    /// (From, To) pairing passes the lead wave's (higher) progress as <paramref name="fromProgress"/> and the trailing wave's
    /// (lower) progress as <paramref name="toProgress"/>; calling with the two swapped produces the
    /// exact reversed run rather than an empty or wrong one, since which fraction is larger decides
    /// direction, not argument position. Clears <paramref name="output"/> first and never allocates
    /// (docs/CONVENTIONS.md) - callers reuse the same list every call, matching
    /// <see cref="ComputeSpineSegments"/>.
    /// </summary>
    public static void ComputeSpinePoints(ArmyPath path, double fromProgress, double toProgress, List<MapPoint> output)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(output);

        output.Clear();

        var waypoints = path.Waypoints;
        var length = path.Length;
        var fromDistance = Math.Clamp(fromProgress, 0.0, 1.0) * length;
        var toDistance = Math.Clamp(toProgress, 0.0, 1.0) * length;

        output.Add(PointAtDistance(waypoints, fromDistance));

        var lowerBound = Math.Min(fromDistance, toDistance);
        var upperBound = Math.Max(fromDistance, toDistance);

        if (toDistance >= fromDistance)
        {
            var cumulative = 0.0;
            for (var i = 0; i < waypoints.Count; i++)
            {
                if (i > 0)
                {
                    cumulative += WaypointDistance(waypoints[i - 1], waypoints[i]);
                }

                if (cumulative > lowerBound && cumulative < upperBound)
                {
                    output.Add(waypoints[i]);
                }
            }
        }
        else
        {
            var cumulative = length;
            for (var i = waypoints.Count - 1; i >= 0; i--)
            {
                if (i < waypoints.Count - 1)
                {
                    cumulative -= WaypointDistance(waypoints[i], waypoints[i + 1]);
                }

                if (cumulative > lowerBound && cumulative < upperBound)
                {
                    output.Add(waypoints[i]);
                }
            }
        }

        output.Add(PointAtDistance(waypoints, toDistance));
    }

    /// <summary>
    /// The point at arc-length <paramref name="distance"/> along <paramref name="waypoints"/>,
    /// mirroring <c>Match.PositionAlongPath</c>'s own arithmetic but keyed by distance rather than a
    /// 0..1 fraction, since a spine segment's two ends are not always the whole path's endpoints.
    /// </summary>
    private static MapPoint PointAtDistance(IReadOnlyList<MapPoint> waypoints, double distance)
    {
        if (distance <= 0.0)
        {
            return waypoints[0];
        }

        var accumulated = 0.0;
        for (var i = 1; i < waypoints.Count; i++)
        {
            var segmentStart = waypoints[i - 1];
            var segmentEnd = waypoints[i];
            var segmentLength = WaypointDistance(segmentStart, segmentEnd);

            if (accumulated + segmentLength >= distance || i == waypoints.Count - 1)
            {
                var remaining = distance - accumulated;
                var segmentFraction = segmentLength > 0.0 ? Math.Clamp(remaining / segmentLength, 0.0, 1.0) : 0.0;
                return new MapPoint(
                    segmentStart.X + ((segmentEnd.X - segmentStart.X) * segmentFraction),
                    segmentStart.Y + ((segmentEnd.Y - segmentStart.Y) * segmentFraction));
            }

            accumulated += segmentLength;
        }

        return waypoints[waypoints.Count - 1];
    }

    private static double WaypointDistance(MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
