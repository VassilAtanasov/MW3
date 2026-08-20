namespace MW3.Protocol;

/// <summary>
/// Where an army is, and how far along it is, as pure functions of its <see cref="ArmyPath"/> and
/// its own launch and arrival ticks (D-68). One implementation, three readers: the rules
/// (<c>Match.PositionOf</c>/<c>ProgressOf</c> and tower-range evaluation), the renderer, and - once
/// FR-3 lands - a client that has a snapshot and no <c>Match</c> at all.
///
/// This is why the wire never carries an army position. An army's position is a function of data
/// that never changes after launch (D-39, D-51) plus the current tick, so launch data alone renders
/// an army forever, at whatever frame rate the client draws at and however rarely the server sends.
/// Putting the position on the wire instead would make smooth motion depend on the send rate, and
/// would put the same arithmetic on both sides anyway - the drift shape #68, phase 5's morale patch
/// and D-45 each had to close once.
/// </summary>
public static class ArmyPathMath
{
    /// <summary>
    /// An army's clamped 0..1 flight fraction at <paramref name="currentTick"/> - the one place
    /// launch and arrival ticks are turned into a fraction, so no two callers can disagree. Clamped
    /// rather than extrapolated: a tick before launch or after arrival resolves to an endpoint.
    /// A zero-or-negative span (an arrival on its own launch tick) is fully arrived, not a division
    /// by zero.
    /// </summary>
    public static double ProgressAt(long launchTick, long arrivalTick, long currentTick)
    {
        var span = arrivalTick - launchTick;
        var fraction = span > 0 ? (double)(currentTick - launchTick) / span : 1.0;
        return Math.Clamp(fraction, 0.0, 1.0);
    }

    /// <summary>
    /// The point at arc-length fraction <paramref name="fraction"/> (0..1) along
    /// <paramref name="path"/>'s waypoints. At 0 this is exactly the first waypoint and at 1 exactly
    /// the last, regardless of accumulated floating-point drift along the way.
    /// </summary>
    public static MapPoint PositionAtProgress(ArmyPath path, double fraction)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var waypoints = path.Waypoints;
        if (fraction <= 0.0)
        {
            return waypoints[0];
        }

        if (fraction >= 1.0)
        {
            return waypoints[waypoints.Count - 1];
        }

        var targetDistance = fraction * path.Length;
        var accumulated = 0.0;

        for (var i = 1; i < waypoints.Count; i++)
        {
            var segmentStart = waypoints[i - 1];
            var segmentEnd = waypoints[i];
            var dx = segmentEnd.X - segmentStart.X;
            var dy = segmentEnd.Y - segmentStart.Y;
            var segmentLength = Math.Sqrt((dx * dx) + (dy * dy));

            // The `i == last` arm catches the case where accumulated segment lengths fall a
            // floating-point hair short of targetDistance on the final segment: without it the loop
            // would fall out of the bottom and snap to the last waypoint. Sqrt is the only maths
            // beyond the four operations anywhere in this project, and it is IEEE-754 exact (D-60).
            if (accumulated + segmentLength >= targetDistance || i == waypoints.Count - 1)
            {
                var remaining = targetDistance - accumulated;
                var segmentFraction = segmentLength > 0.0 ? Math.Clamp(remaining / segmentLength, 0.0, 1.0) : 0.0;
                return new MapPoint(
                    segmentStart.X + (dx * segmentFraction),
                    segmentStart.Y + (dy * segmentFraction));
            }

            accumulated += segmentLength;
        }

        return waypoints[waypoints.Count - 1];
    }

    /// <summary>
    /// An army's normalized-space position at <paramref name="currentTick"/>: a pure function of its
    /// path and its own launch and arrival ticks, recomputed fresh every time rather than
    /// accumulated. Walks the polyline at uniform speed - at elapsed fraction <c>f</c> of the flight
    /// the army sits at arc-length <c>f * Length</c> along the waypoints.
    /// </summary>
    public static MapPoint PositionAt(ArmyPath path, long launchTick, long arrivalTick, long currentTick) =>
        PositionAtProgress(path, ProgressAt(launchTick, arrivalTick, currentTick));
}
