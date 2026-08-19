namespace MW3.Protocol;

/// <summary>
/// Element-wise equality for the read-only lists the snapshot records carry. A positional record
/// compares its members with <see cref="object.Equals(object, object)"/>, which is reference
/// equality for a list - so a snapshot that has been round-tripped through JSON would never compare
/// equal to the one it was built from, however faithful the round trip was. The snapshot's own
/// definition of "equal" has to be structural, because that is the claim FR-2 will diff against and
/// FR-4 will send over a wire.
/// </summary>
internal static class SnapshotEquality
{
    public static bool ListEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A hash over a list's elements, matching <see cref="ListEquals"/>. Order matters, as it does
    /// for equality - base, army and waypoint order are part of what a snapshot asserts.
    /// </summary>
    public static int ListHash<T>(IReadOnlyList<T> items)
    {
        if (items is null)
        {
            return 0;
        }

        var hash = 17;
        for (var i = 0; i < items.Count; i++)
        {
            hash = unchecked((hash * 31) + (items[i]?.GetHashCode() ?? 0));
        }

        return hash;
    }
}
