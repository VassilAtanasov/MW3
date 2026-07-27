namespace MW3.Core;

/// <summary>
/// Answers "which base, if any, is at this normalized point" (D-18) - the one part of the
/// send-army drag that must be testable without a graphics device. Reads only each base's id and
/// position; never touches garrison or owner.
/// </summary>
public static class HitTester
{
    /// <summary>
    /// Normalized-space distance within which a point resolves to its nearest base. Named so a test
    /// can assert no two bases in the hardcoded map lie within twice this of each other - the
    /// constant that keeps the nearest match from ever being ambiguous.
    /// </summary>
    public const double SelectionThresholdUnits = 0.1;

    /// <summary>
    /// The nearest base to <paramref name="point"/>, or null if even the nearest one is farther than
    /// <see cref="SelectionThresholdUnits"/>.
    /// </summary>
    public static int? FindBaseAt(MapPoint point, IReadOnlyList<Base> bases)
    {
        if (bases is null)
        {
            throw new ArgumentNullException(nameof(bases));
        }

        var nearestId = FindNearestBaseId(point, bases, out var nearestDistance);
        return nearestId is not null && nearestDistance <= SelectionThresholdUnits ? nearestId : null;
    }

    /// <summary>
    /// The genuinely nearest base regardless of distance, with no threshold applied - kept private
    /// and reached by reflection in tests (mirroring <c>Match.ComputeTravelTicks</c>) so the
    /// "resolves to the nearer one" case can be asserted independently of the threshold gate.
    /// </summary>
    private static int? FindNearestBaseId(MapPoint point, IReadOnlyList<Base> bases, out double nearestDistance)
    {
        int? nearestId = null;
        nearestDistance = double.MaxValue;

        // Indexed rather than foreach: `bases` is IReadOnlyList<Base>, and enumerating a List<T>
        // through that interface boxes its struct enumerator on every call - not acceptable in a
        // hit-test reached from every drag press and release (docs/CONVENTIONS.md).
        for (var i = 0; i < bases.Count; i++)
        {
            var b = bases[i];
            var dx = b.Position.X - point.X;
            var dy = b.Position.Y - point.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestId = b.Id;
            }
        }

        return nearestId;
    }
}
