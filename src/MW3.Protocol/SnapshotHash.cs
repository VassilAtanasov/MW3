using System.Text;

namespace MW3.Protocol;

/// <summary>
/// A stable hash of a <see cref="MatchSnapshot"/>, over a canonical field-by-field serialization
/// (D-71). Built with FNV-1a over each field's own bytes, never <see cref="object.GetHashCode"/> or
/// <see cref="string.GetHashCode()"/> - .NET randomizes string hashing per process, so a hash built
/// on it would differ between two runs of the very same program on the very same machine, which
/// would make a golden-hash test meaningless. Every field this walks is either a primitive, an
/// enum, or already visited by <see cref="SnapshotDiffer"/> and <see cref="SnapshotApplier"/>, so
/// the walk order here mirrors those rather than inventing a third one.
/// </summary>
public static class SnapshotHash
{
    private const ulong _offsetBasis = 14695981039346656037UL;
    private const ulong _prime = 1099511628211UL;

    /// <summary>Computes the hash. Deterministic across calls, and across processes on the same platform family (D-71).</summary>
    public static ulong Compute(MatchSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var hash = _offsetBasis;
        hash = HashInt(hash, snapshot.ProtocolVersion);
        hash = HashString(hash, snapshot.MapId);
        hash = HashLong(hash, snapshot.ElapsedTicks);
        hash = HashInt(hash, (int)snapshot.Outcome);
        hash = HashInt(hash, snapshot.LocalPlayerId);

        hash = HashInt(hash, snapshot.Obstacles.Count);
        foreach (var obstacle in snapshot.Obstacles)
        {
            hash = HashObstacle(hash, obstacle);
        }

        hash = HashInt(hash, snapshot.Players.Count);
        foreach (var player in snapshot.Players)
        {
            hash = HashPlayer(hash, player);
        }

        hash = HashInt(hash, snapshot.Bases.Count);
        foreach (var b in snapshot.Bases)
        {
            hash = HashBase(hash, b);
        }

        hash = HashInt(hash, snapshot.Armies.Count);
        foreach (var army in snapshot.Armies)
        {
            hash = HashArmy(hash, army);
        }

        return hash;
    }

    private static ulong HashObstacle(ulong hash, MapObstacle o)
    {
        hash = HashDouble(hash, o.MinX);
        hash = HashDouble(hash, o.MinY);
        hash = HashDouble(hash, o.MaxX);
        return HashDouble(hash, o.MaxY);
    }

    private static ulong HashPoint(ulong hash, MapPoint p)
    {
        hash = HashDouble(hash, p.X);
        return HashDouble(hash, p.Y);
    }

    private static ulong HashPlayer(ulong hash, PlayerSnapshot p)
    {
        hash = HashInt(hash, p.Id);
        hash = HashInt(hash, (int)p.ControllerKind);
        hash = HashInt(hash, p.MoralePoints);
        hash = HashInt(hash, p.MoraleLevel);
        hash = HashInt(hash, p.MoraleAttackPercentage);
        hash = HashInt(hash, p.MoraleDefencePercentage);
        hash = HashInt(hash, p.ForgeCount);
        hash = HashInt(hash, p.ForgeAttackPercentage);
        return HashInt(hash, p.ForgeDefencePercentage);
    }

    private static ulong HashBase(ulong hash, BaseSnapshot b)
    {
        hash = HashInt(hash, b.Id);
        hash = HashPoint(hash, b.Position);
        hash = HashNullableInt(hash, b.OwnerPlayerId);
        hash = HashInt(hash, (int)b.Type);
        hash = HashInt(hash, b.Level);
        hash = HashInt(hash, b.GarrisonCount);
        hash = HashNullableInt(hash, b.GarrisonCap);
        hash = HashNullableInt(hash, b.UpgradeCost);
        hash = HashInt(hash, b.DefencePercentage);
        hash = HashDouble(hash, b.RingThicknessFractionOfRadius);
        hash = HashInt(hash, b.MaxLevel);
        hash = HashInt(hash, b.MaxUpgradableLevel);
        hash = HashLong(hash, b.ProductionProgressTicks);
        hash = HashConstruction(hash, b.Construction);
        hash = HashNullableLong(hash, b.LastOwnerChangeTick);
        hash = HashNullableInt(hash, b.OwnerBeforeLastChangePlayerId);
        hash = HashNullableLong(hash, b.LastFireTick);

        hash = HashInt(hash, b.AvailableActions.Count);
        foreach (var action in b.AvailableActions)
        {
            hash = HashInt(hash, (int)action.Kind);
            hash = HashInt(hash, action.Cost);
            hash = HashInt(hash, (int)action.Availability);
            hash = HashNullableInt(hash, action.ConvertTargetType.HasValue ? (int)action.ConvertTargetType.Value : (int?)null);
        }

        return hash;
    }

    private static ulong HashConstruction(ulong hash, PendingConstructionSnapshot? construction)
    {
        hash = HashBool(hash, construction is not null);
        if (construction is null)
        {
            return hash;
        }

        hash = HashInt(hash, (int)construction.Kind);
        hash = HashLong(hash, construction.CompletionTick);
        hash = HashNullableInt(hash, construction.TargetLevel);
        return HashNullableInt(hash, construction.TargetType.HasValue ? (int)construction.TargetType.Value : (int?)null);
    }

    private static ulong HashArmy(ulong hash, ArmySnapshot a)
    {
        hash = HashInt(hash, a.Id);
        hash = HashInt(hash, a.OwnerPlayerId);
        hash = HashInt(hash, a.SourceBaseId);
        hash = HashInt(hash, a.TargetBaseId);
        hash = HashInt(hash, a.UnitCount);
        hash = HashLong(hash, a.LaunchTick);
        hash = HashLong(hash, a.ArrivalTick);
        hash = HashInt(hash, a.SendId);
        hash = HashInt(hash, a.WaveIndex);
        hash = HashInt(hash, a.WaveCount);
        hash = HashInt(hash, a.PathWaypoints.Count);
        foreach (var waypoint in a.PathWaypoints)
        {
            hash = HashPoint(hash, waypoint);
        }

        return HashDouble(hash, a.PathLength);
    }

    private static ulong HashByte(ulong hash, byte b) => unchecked((hash ^ b) * _prime);

    private static ulong HashBytes(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            hash = HashByte(hash, b);
        }

        return hash;
    }

    private static ulong HashBool(ulong hash, bool value) => HashByte(hash, value ? (byte)1 : (byte)0);

    private static ulong HashInt(ulong hash, int value) => HashBytes(hash, BitConverter.GetBytes(value));

    private static ulong HashLong(ulong hash, long value) => HashBytes(hash, BitConverter.GetBytes(value));

    private static ulong HashDouble(ulong hash, double value) => HashBytes(hash, BitConverter.GetBytes(value));

    private static ulong HashNullableInt(ulong hash, int? value)
    {
        hash = HashBool(hash, value.HasValue);
        return value.HasValue ? HashInt(hash, value.Value) : hash;
    }

    private static ulong HashNullableLong(ulong hash, long? value)
    {
        hash = HashBool(hash, value.HasValue);
        return value.HasValue ? HashLong(hash, value.Value) : hash;
    }

    private static ulong HashString(ulong hash, string? value)
    {
        hash = HashBool(hash, value is not null);
        if (value is null)
        {
            return hash;
        }

        hash = HashInt(hash, value.Length);
        return HashBytes(hash, Encoding.UTF8.GetBytes(value));
    }
}
