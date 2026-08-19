namespace MW3.Core;

/// <summary>
/// Turns a live <see cref="Match"/> into a <see cref="MatchSnapshot"/> for one player (FR-1). It
/// reads and never writes: building a snapshot cannot change a match's state or its subsequent
/// behaviour, which is what lets the server build one every tick without the act of observing the
/// simulation being part of it.
///
/// It lives in <c>MW3.Core</c> rather than in <c>MW3.Protocol</c> on purpose. Building a snapshot
/// means reading <c>LevelTable</c>, <c>MoraleTable</c> and <c>ForgeTable</c> to resolve the derived
/// values the client is given rather than told how to compute; <c>MW3.Protocol</c> must not be able
/// to reach a table, and D-57's missing project reference is what guarantees it cannot.
/// </summary>
public static class MatchSnapshotBuilder
{
    /// <summary>
    /// Builds a snapshot of <paramref name="match"/> as <paramref name="localPlayer"/> sees it.
    /// Available actions are populated only for bases <paramref name="localPlayer"/> owns (D-66);
    /// everything else in the snapshot is the same for every player, because MW3 has no fog of war.
    /// </summary>
    public static MatchSnapshot Build(Match match, Player localPlayer)
    {
        if (match is null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        if (localPlayer is null)
        {
            throw new ArgumentNullException(nameof(localPlayer));
        }

        // A player from some other match would produce a snapshot that is quietly wrong rather than
        // loudly broken: LocalPlayerId would name nobody in Players, and every base's action list
        // would come back empty, because AvailableActions short-circuits on ownership. A server
        // holding many matches at once (FR-4) is exactly the caller that can make this mistake.
        if (localPlayer != match.HumanPlayer && localPlayer != match.AiPlayer)
        {
            throw new ArgumentException("The local player is not one of this match's players.", nameof(localPlayer));
        }

        var players = new List<PlayerSnapshot>(2)
        {
            BuildPlayer(match, match.HumanPlayer),
            BuildPlayer(match, match.AiPlayer),
        };

        var bases = new List<BaseSnapshot>(match.Bases.Count);
        foreach (var b in match.Bases)
        {
            bases.Add(BuildBase(match, b, localPlayer));
        }

        var armies = new List<ArmySnapshot>(match.ArmiesInFlight.Count);
        foreach (var army in match.ArmiesInFlight)
        {
            armies.Add(BuildArmy(army));
        }

        var obstacles = new List<MapObstacle>(match.Obstacles.Count);
        foreach (var obstacle in match.Obstacles)
        {
            obstacles.Add(obstacle);
        }

        return new MatchSnapshot(
            MatchSnapshot.CurrentProtocolVersion,
            match.MapId?.ToString(),
            match.ElapsedTicks,
            match.Outcome,
            localPlayer.Id,
            obstacles,
            players,
            bases,
            armies);
    }

    private static PlayerSnapshot BuildPlayer(Match match, Player player)
    {
        var morale = match.MoraleFor(player);
        var forges = match.ForgeCountFor(player);

        return new PlayerSnapshot(
            player.Id,
            player.ControllerKind,
            morale.Points,
            morale.Level,
            MoraleTable.AttackPercentage(morale.Level),
            MoraleTable.DefencePercentage(morale.Level),
            forges,
            ForgeTable.AttackPercentage(forges),
            ForgeTable.DefencePercentage(forges));
    }

    private static BaseSnapshot BuildBase(Match match, Base b, Player localPlayer)
    {
        // AvailableActions already answers "nothing" for a base the player does not own, so the
        // ownership rule is expressed once, in the rules, rather than restated here.
        var actions = match.AvailableActions(localPlayer, b.Id);
        var actionSnapshots = new List<BaseActionSnapshot>(actions.Count);
        foreach (var action in actions)
        {
            actionSnapshots.Add(new BaseActionSnapshot(action.Kind, action.Cost, action.Availability, action.ConvertTargetType));
        }

        return new BaseSnapshot(
            b.Id,
            b.Position,
            b.Owner?.Id,
            b.Type,
            b.Level,
            b.GarrisonCount,
            b.GarrisonCap,
            UpgradeCostOrNull(b),
            b.DefencePercentage,
            b.RingThicknessFractionOfRadius,
            b.MaxLevel,
            b.MaxUpgradableLevel,
            b.ProductionProgressTicks,
            BuildConstruction(b.Construction),
            b.LastOwnerChangeTick,
            b.OwnerBeforeLastChange?.Id,
            b.LastFireTick,
            actionSnapshots);
    }

    /// <summary>
    /// A base's next-level price, or null where the ladder does not define one. <c>Base.UpgradeCost</c>
    /// throws above a type's maximum upgradable level and for a forge, which has no upgrade path at
    /// all - so the absence is answered by the same gate <c>AvailableActions</c> applies, never by
    /// catching the exception.
    /// </summary>
    private static int? UpgradeCostOrNull(Base b) =>
        b.Level < b.MaxUpgradableLevel ? b.UpgradeCost : null;

    private static PendingConstructionSnapshot? BuildConstruction(PendingConstruction? construction) => construction switch
    {
        null => null,
        PendingUpgrade upgrade => new PendingConstructionSnapshot(
            BaseActionKind.Upgrade,
            upgrade.CompletionTick,
            upgrade.TargetLevel,
            TargetType: null),
        PendingConversion conversion => new PendingConstructionSnapshot(
            BaseActionKind.Convert,
            conversion.CompletionTick,
            TargetLevel: null,
            conversion.TargetType),
        _ => throw new ArgumentOutOfRangeException(
            nameof(construction),
            construction,
            "The snapshot has no shape for this kind of pending construction."),
    };

    private static ArmySnapshot BuildArmy(Army army)
    {
        // The waypoints are copied rather than shared: every wave of a send holds the same ArmyPath
        // instance (D-51), and a snapshot that handed three armies one list would make a receiver's
        // idea of "these are separate armies" depend on aliasing that does not survive a wire.
        var waypoints = new List<MapPoint>(army.Path.Waypoints.Count);
        foreach (var waypoint in army.Path.Waypoints)
        {
            waypoints.Add(waypoint);
        }

        return new ArmySnapshot(
            army.Id,
            army.Owner.Id,
            army.SourceBaseId,
            army.TargetBaseId,
            army.UnitCount,
            army.LaunchTick,
            army.ArrivalTick,
            army.SendId,
            army.WaveIndex,
            army.WaveCount,
            waypoints,
            army.Path.Length);
    }
}
