namespace MW3.Protocol;

/// <summary>
/// The polyline an army flies (phase 7 FR-3), computed once by <c>MW3.Core</c>'s <c>PathCalculator</c>
/// at a send's submission tick and shared, unchanged, by every wave of that send (D-51). A value: no
/// behaviour, no mutation after construction. Named in prose rather than by <c>cref</c> because
/// <c>MW3.Protocol</c> cannot reference the rules (D-57), which is the point.
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
