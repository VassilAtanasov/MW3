namespace MW3.Protocol;

/// <summary>
/// An axis-aligned rectangle in normalized 0..1 map units (D-50) that a <c>MapDefinition</c>
/// carries and a <see cref="MatchSnapshot"/> reproduces. Blocks movement (phase 7 FR-3) and is drawn
/// (phase 7 FR-4).
/// </summary>
public readonly struct MapObstacle
{
    public MapObstacle(double minX, double minY, double maxX, double maxY)
    {
        if (maxX <= minX)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"An obstacle's max X ({maxX}) must be greater than its min X ({minX})."),
                nameof(maxX));
        }

        if (maxY <= minY)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"An obstacle's max Y ({maxY}) must be greater than its min Y ({minY})."),
                nameof(maxY));
        }

        if (minX < 0.0 || maxX > 1.0)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"An obstacle's X extent ({minX} to {maxX}) must stay within the map's 0..1 range."),
                nameof(minX));
        }

        if (minY < 0.0 || maxY > 1.0)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"An obstacle's Y extent ({minY} to {maxY}) must stay within the map's 0..1 range."),
                nameof(minY));
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public bool Contains(MapPoint point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}
