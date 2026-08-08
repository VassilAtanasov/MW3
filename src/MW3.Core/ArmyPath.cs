namespace MW3.Core;

/// <summary>
/// The polyline an <see cref="Army"/> flies (FR-3), computed once by <see cref="PathCalculator"/> at
/// a send's submission tick and shared, unchanged, by every wave of that send (D-51). A value: no
/// behaviour, no mutation after construction.
/// </summary>
public sealed class ArmyPath
{
    public ArmyPath(IReadOnlyList<MapPoint> waypoints, double length)
    {
        if (waypoints is null)
        {
            throw new ArgumentNullException(nameof(waypoints));
        }

        if (waypoints.Count < 2)
        {
            throw new ArgumentException("An army path must have at least two waypoints.", nameof(waypoints));
        }

        Waypoints = waypoints;
        Length = length;
    }

    public IReadOnlyList<MapPoint> Waypoints { get; }

    public double Length { get; }
}
