namespace MW3.Core.Tests;

/// <summary>FR-1: <see cref="MapObstacle"/> is an axis-aligned rectangle in normalized 0..1 space (D-50).</summary>
public class MapObstacleTests
{
    [Fact]
    public void Constructor_RejectsInvertedXExtent_NamingTheXAxis()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.6, minY: 0.1, maxX: 0.4, maxY: 0.5));
        Assert.Contains("X", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsDegenerateXExtent_NamingTheXAxis()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.5, minY: 0.1, maxX: 0.5, maxY: 0.5));
        Assert.Contains("X", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsInvertedYExtent_NamingTheYAxis()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.1, minY: 0.6, maxX: 0.5, maxY: 0.4));
        Assert.Contains("Y", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsDegenerateYExtent_NamingTheYAxis()
    {
        var ex = Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.1, minY: 0.5, maxX: 0.5, maxY: 0.5));
        Assert.Contains("Y", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsARectangleFallingOutsideTheMapOnX()
    {
        Assert.Throws<ArgumentException>(() => new MapObstacle(minX: -0.1, minY: 0.1, maxX: 0.5, maxY: 0.5));
        Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.5, minY: 0.1, maxX: 1.1, maxY: 0.5));
    }

    [Fact]
    public void Constructor_RejectsARectangleFallingOutsideTheMapOnY()
    {
        Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.1, minY: -0.1, maxX: 0.5, maxY: 0.5));
        Assert.Throws<ArgumentException>(() => new MapObstacle(minX: 0.1, minY: 0.5, maxX: 0.5, maxY: 1.1));
    }

    [Fact]
    public void Constructor_AcceptsAWellFormedRectangleWithinTheMap()
    {
        var obstacle = new MapObstacle(minX: 0.42, minY: 0.30, maxX: 0.58, maxY: 0.70);
        Assert.Equal(0.42, obstacle.MinX);
        Assert.Equal(0.30, obstacle.MinY);
        Assert.Equal(0.58, obstacle.MaxX);
        Assert.Equal(0.70, obstacle.MaxY);
    }

    [Fact]
    public void Contains_IsTrueForAPointInside_AndFalseForAPointOutside()
    {
        var obstacle = new MapObstacle(minX: 0.42, minY: 0.30, maxX: 0.58, maxY: 0.70);

        Assert.True(obstacle.Contains(new MapPoint(0.50, 0.50)));
        Assert.False(obstacle.Contains(new MapPoint(0.10, 0.10)));
    }
}
