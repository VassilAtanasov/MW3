namespace MW3.Core;

/// <summary>
/// Pure geometry (FR-3): a visibility-graph pathfinder over two endpoints and a map's obstacles.
/// Holds no state, reads no <see cref="Match"/>, and is a pure function of its arguments - like
/// <see cref="TowerThreatEstimator"/>. Called once, at a send's submission tick (D-51), never
/// cached or precomputed per map - at nine bases and four corners the cost is irrelevant and a
/// cache is just a place for a stale path to hide.
/// </summary>
public static class PathCalculator
{
    /// <summary>
    /// How far outside an obstacle's corner a routing node sits (D-52, "Tuning values"), so a route
    /// grazing the corner never counts as touching the obstacle itself.
    /// </summary>
    public const double CornerInset = 0.02;

    private const double _epsilon = 1e-9;

    /// <summary>
    /// The shortest route from <paramref name="from"/> to <paramref name="to"/> around every one of
    /// <paramref name="obstacles"/> that actually blocks the straight line between them. Never
    /// rejects: if the visibility graph leaves <paramref name="to"/> unreachable (only possible for a
    /// test-injected layout that walls a base in), the straight two-waypoint path is returned instead
    /// of throwing - a send is never refused for being blocked.
    /// </summary>
    public static ArmyPath ComputePath(MapPoint from, MapPoint to, IReadOnlyList<MapObstacle> obstacles)
    {
        if (obstacles is null)
        {
            throw new ArgumentNullException(nameof(obstacles));
        }

        var nodes = BuildNodes(from, to, obstacles);
        var edges = BuildEdges(nodes, obstacles);
        var routeIndices = FindShortestRouteIndices(nodes, edges);

        if (routeIndices is null)
        {
            return StraightPath(from, to);
        }

        var waypoints = new MapPoint[routeIndices.Count];
        for (var i = 0; i < routeIndices.Count; i++)
        {
            waypoints[i] = nodes[routeIndices[i]];
        }

        return new ArmyPath(waypoints, TotalLength(waypoints));
    }

    private static ArmyPath StraightPath(MapPoint from, MapPoint to) =>
        new(new[] { from, to }, Distance(from, to));

    private static double TotalLength(MapPoint[] waypoints)
    {
        var total = 0.0;
        for (var i = 1; i < waypoints.Length; i++)
        {
            total += Distance(waypoints[i - 1], waypoints[i]);
        }

        return total;
    }

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Node 0 is <paramref name="from"/>, node 1 is <paramref name="to"/>, then each obstacle's four
    /// inset corners in the fixed order <c>(minX,minY)</c>, <c>(minX,maxY)</c>, <c>(maxX,minY)</c>,
    /// <c>(maxX,maxY)</c>, obstacles taken in the map definition's own order (D-52) - never
    /// enumeration order, so the tie-break below is reproducible.
    /// </summary>
    private static List<MapPoint> BuildNodes(MapPoint from, MapPoint to, IReadOnlyList<MapObstacle> obstacles)
    {
        var nodes = new List<MapPoint> { from, to };
        foreach (var obstacle in obstacles)
        {
            nodes.Add(new MapPoint(obstacle.MinX - CornerInset, obstacle.MinY - CornerInset));
            nodes.Add(new MapPoint(obstacle.MinX - CornerInset, obstacle.MaxY + CornerInset));
            nodes.Add(new MapPoint(obstacle.MaxX + CornerInset, obstacle.MinY - CornerInset));
            nodes.Add(new MapPoint(obstacle.MaxX + CornerInset, obstacle.MaxY + CornerInset));
        }

        return nodes;
    }

    private static bool[,] BuildEdges(List<MapPoint> nodes, IReadOnlyList<MapObstacle> obstacles)
    {
        var n = nodes.Count;
        var edges = new bool[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var blocked = false;
                foreach (var obstacle in obstacles)
                {
                    if (SegmentCrossesInterior(nodes[i], nodes[j], obstacle))
                    {
                        blocked = true;
                        break;
                    }
                }

                edges[i, j] = !blocked;
                edges[j, i] = !blocked;
            }
        }

        return edges;
    }

    /// <summary>
    /// True when segment <paramref name="p0"/>-<paramref name="p1"/> passes through
    /// <paramref name="obstacle"/>'s strict interior. A segment that only touches the obstacle's
    /// boundary edge or a corner does not count - without this, the inset corner nodes themselves
    /// would be unusable on a graze.
    /// </summary>
    private static bool SegmentCrossesInterior(MapPoint p0, MapPoint p1, MapObstacle obstacle)
    {
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;

        var t0 = 0.0;
        var t1 = 1.0;
        if (!ClipTest(-dx, p0.X - obstacle.MinX, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTest(dx, obstacle.MaxX - p0.X, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTest(-dy, p0.Y - obstacle.MinY, ref t0, ref t1))
        {
            return false;
        }

        if (!ClipTest(dy, obstacle.MaxY - p0.Y, ref t0, ref t1))
        {
            return false;
        }

        if (t1 - t0 < _epsilon)
        {
            // No overlap with the closed rectangle, or a single touching point - neither enters
            // the interior.
            return false;
        }

        var midT = (t0 + t1) / 2.0;
        var midX = p0.X + (dx * midT);
        var midY = p0.Y + (dy * midT);

        return midX > obstacle.MinX + _epsilon && midX < obstacle.MaxX - _epsilon
            && midY > obstacle.MinY + _epsilon && midY < obstacle.MaxY - _epsilon;
    }

    /// <summary>The standard Liang-Barsky boundary clip against one of the rectangle's four half-planes.</summary>
    private static bool ClipTest(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0.0)
        {
            return q >= 0.0;
        }

        var r = q / p;
        if (p < 0.0)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }

    /// <summary>
    /// The shortest simple route from node 0 to node 1, found by exhaustively enumerating every
    /// simple path through the visibility graph rather than a shortest-path algorithm like Dijkstra
    /// (D-52) - an exact-length tie must be broken by comparing whole candidate routes'
    /// node-index sequences, not resolved incidentally by relaxation order. Exhaustive enumeration is
    /// affordable because the graph is tiny: two endpoints plus four corners per obstacle, and this
    /// phase ships at most one obstacle per map. Returns null if no route connects the two nodes at
    /// all.
    /// </summary>
    private static List<int>? FindShortestRouteIndices(List<MapPoint> nodes, bool[,] edges)
    {
        const int fromIndex = 0;
        const int toIndex = 1;

        List<int>? best = null;
        var bestLength = double.MaxValue;
        var visited = new bool[nodes.Count];
        var path = new List<int> { fromIndex };
        visited[fromIndex] = true;

        void Visit(int current, double lengthSoFar)
        {
            if (current == toIndex)
            {
                if (lengthSoFar < bestLength - _epsilon)
                {
                    bestLength = lengthSoFar;
                    best = new List<int>(path);
                }
                else if (lengthSoFar < bestLength + _epsilon && (best is null || IsLexicographicallySmaller(path, best)))
                {
                    best = new List<int>(path);
                    bestLength = Math.Min(bestLength, lengthSoFar);
                }

                return;
            }

            for (var next = 0; next < nodes.Count; next++)
            {
                if (visited[next] || !edges[current, next])
                {
                    continue;
                }

                visited[next] = true;
                path.Add(next);
                Visit(next, lengthSoFar + Distance(nodes[current], nodes[next]));
                path.RemoveAt(path.Count - 1);
                visited[next] = false;
            }
        }

        Visit(fromIndex, 0.0);
        return best;
    }

    private static bool IsLexicographicallySmaller(List<int> a, List<int> b)
    {
        var length = Math.Min(a.Count, b.Count);
        for (var i = 0; i < length; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] < b[i];
            }
        }

        return a.Count < b.Count;
    }
}
