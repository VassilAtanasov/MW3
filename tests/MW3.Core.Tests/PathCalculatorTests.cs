namespace MW3.Core.Tests;

/// <summary>
/// FR-3: <see cref="PathCalculator"/> is a pure visibility-graph pathfinder over two endpoints and a
/// map's obstacles (<c>docs/maps/REQUIREMENTS.md</c> FR-3, <c>docs/maps/ARCHITECTURE.md</c> D-52,
/// D-53). Covers the empty/non-blocking straight-path identity, the routed detour around Medium's
/// obstacle, the D-52 deterministic tie-break, and the never-reject fallback.
/// </summary>
public class PathCalculatorTests
{
    private static readonly MapPoint _humanStart = new(0.12, 0.50);
    private static readonly MapPoint _aiStart = new(0.88, 0.50);

    [Fact]
    public void EmptyObstacleList_ReturnsTheTwoPointStraightPath()
    {
        var path = PathCalculator.ComputePath(_humanStart, _aiStart, Array.Empty<MapObstacle>());

        Assert.Equal(new[] { _humanStart, _aiStart }, path.Waypoints);
        Assert.Equal(0.76, path.Length, precision: 12);
    }

    [Fact]
    public void ObstacleNotIntersectingTheStraightSegment_StillReturnsTheTwoPointPath()
    {
        // Sits well clear of the human-to-AI-start line (y=0.50).
        var obstacle = new MapObstacle(minX: 0.42, minY: 0.05, maxX: 0.58, maxY: 0.15);

        var path = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { obstacle });

        Assert.Equal(new[] { _humanStart, _aiStart }, path.Waypoints);
        Assert.Equal(Distance(_humanStart, _aiStart), path.Length, precision: 12);
    }

    [Fact]
    public void MediumObstacle_RoutesAroundItAndMatchesTheKickoffsPublishedLengths()
    {
        var path = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { MapCatalog.Medium.Obstacles[0] });

        // docs/maps/REQUIREMENTS.md FR-3's kickoff: 0.912 routed against 0.760 straight.
        Assert.Equal(0.912, path.Length, precision: 3);
        Assert.True(path.Length > Distance(_humanStart, _aiStart));
    }

    [Fact]
    public void ReturnedPath_BeginsAtFromAndEndsAtTo_WithNoDuplicateConsecutivePointsAndConsistentLength()
    {
        var path = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { MapCatalog.Medium.Obstacles[0] });

        Assert.Equal(_humanStart, path.Waypoints[0]);
        Assert.Equal(_aiStart, path.Waypoints[^1]);

        for (var i = 1; i < path.Waypoints.Count; i++)
        {
            Assert.NotEqual(path.Waypoints[i - 1], path.Waypoints[i]);
        }

        var summed = 0.0;
        for (var i = 1; i < path.Waypoints.Count; i++)
        {
            summed += Distance(path.Waypoints[i - 1], path.Waypoints[i]);
        }

        Assert.Equal(summed, path.Length, precision: 12);
    }

    [Fact]
    public void NoRouteExists_ReturnsTheStraightTwoPointPathInstead_RatherThanThrowing()
    {
        // A test-injected layout that walls `to` in: the obstacle's interior contains `to` itself
        // (something no shipped map can produce - MapDefinition rejects a slot inside an obstacle),
        // so every candidate edge into `to`, from `from` or from any inset corner, necessarily enters
        // the interior on its way there and is blocked. `to` ends up isolated - no route exists - and
        // the calculator must still return something rather than throw or return nothing (a send is
        // never rejected for being blocked).
        var enclosing = new MapObstacle(minX: 0.80, minY: 0.40, maxX: 0.96, maxY: 0.60);

        var exception = Record.Exception(() => PathCalculator.ComputePath(_humanStart, _aiStart, new[] { enclosing }));
        Assert.Null(exception);

        var path = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { enclosing });
        Assert.Equal(new[] { _humanStart, _aiStart }, path.Waypoints);
        Assert.Equal(Distance(_humanStart, _aiStart), path.Length, precision: 12);
    }

    // --- Determinism and the tie-break (D-52) ---

    [Fact]
    public void MediumTie_PicksTheLowerYSide_ByTheLexicographicallySmallerNodeIndexSequence()
    {
        var obstacle = MapCatalog.Medium.Obstacles[0];
        var path = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { obstacle });

        Assert.Equal(4, path.Waypoints.Count);
        AssertClose(new MapPoint(0.12, 0.50), path.Waypoints[0]);
        AssertClose(new MapPoint(0.40, 0.28), path.Waypoints[1]);
        AssertClose(new MapPoint(0.60, 0.28), path.Waypoints[2]);
        AssertClose(new MapPoint(0.88, 0.50), path.Waypoints[3]);
    }

    [Fact]
    public void CallingRepeatedlyWithTheSameArguments_ReturnsTheIdenticalWaypointSequenceEveryTime()
    {
        var obstacle = MapCatalog.Medium.Obstacles[0];

        var first = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { obstacle });
        var second = PathCalculator.ComputePath(_humanStart, _aiStart, new[] { obstacle });

        Assert.Equal(first.Waypoints, second.Waypoints);
        Assert.Equal(first.Length, second.Length);
    }

    // --- The crossing test uses the obstacle's strict interior ---

    [Fact]
    public void SegmentGrazingAnObstaclesBoundaryOrCorner_IsNotTreatedAsBlocked()
    {
        // A segment running exactly along the obstacle's top edge (y = minY) touches the boundary
        // but never enters the interior, so it must not be treated as blocked.
        var obstacle = new MapObstacle(minX: 0.40, minY: 0.30, maxX: 0.60, maxY: 0.70);
        var alongTopEdge = PathCalculator.ComputePath(new MapPoint(0.30, 0.30), new MapPoint(0.70, 0.30), new[] { obstacle });

        Assert.Equal(new[] { new MapPoint(0.30, 0.30), new MapPoint(0.70, 0.30) }, alongTopEdge.Waypoints);
    }

    private static void AssertClose(MapPoint expected, MapPoint actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 9);
        Assert.Equal(expected.Y, actual.Y, precision: 9);
    }

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
