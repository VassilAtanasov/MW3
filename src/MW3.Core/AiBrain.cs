namespace MW3.Core;

/// <summary>
/// The AI opponent's brain (D-16, FR-6, FR-7): five clauses evaluated in priority order - defend,
/// upgrade, convert, attack, consolidate - the first that produces a command wins. Every send is
/// <see cref="SendStrengthCalculator.Compute"/> at <see cref="SendStrength.Half"/> (FR-1),
/// identical to the human's rule, so the AI can express nothing a human could not. No lookahead
/// beyond one decision and no randomness
/// (D-15): every clause is a fresh, deterministic read of the match as it stands right now.
/// </summary>
public sealed class AiBrain : IPlayerBrain
{
    public AiBrain(Player player)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        Player = player;
    }

    public Player Player { get; }

    public BrainDecision Decide(Match match)
    {
        if (match is null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        var ownBases = CollectOwnBasesAscendingById(match);

        var decision = TryDefend(match, ownBases);
        if (decision.HasCommand)
        {
            return decision;
        }

        decision = TryUpgrade(match, ownBases);
        if (decision.HasCommand)
        {
            return decision;
        }

        decision = TryConvert(match, ownBases);
        if (decision.HasCommand)
        {
            return decision;
        }

        decision = TryAttack(match, ownBases);
        if (decision.HasCommand)
        {
            return decision;
        }

        return TryConsolidate(match, ownBases);
    }

    /// <summary>
    /// Clause 1: reinforce the lowest-id threatened base from the largest-garrison other own base
    /// that can arrive before the earliest threatening army does. Yields nothing if no base is
    /// threatened, if the sole candidate already has an AI army in flight to it (no
    /// double-targeting), or if no source can arrive in time.
    /// </summary>
    private BrainDecision TryDefend(Match match, List<Base> ownBases)
    {
        var currentTick = match.ElapsedTicks;

        Base? threatened = null;
        var earliestArrival = 0L;

        foreach (var candidate in ownBases)
        {
            if (TryGetThreatenedEarliestArrival(match, candidate, currentTick, out var candidateEarliestArrival))
            {
                threatened = candidate;
                earliestArrival = candidateEarliestArrival;
                break;
            }
        }

        if (threatened is null || AlreadyTargetedByOwnArmy(match, threatened.Id))
        {
            return BrainDecision.None;
        }

        var ticksRemaining = earliestArrival - currentTick;
        Base? source = null;

        foreach (var candidate in ownBases)
        {
            if (candidate.Id == threatened.Id || candidate.GarrisonCount <= 0)
            {
                continue;
            }

            var travelTicks = TravelTimeCalculator.ComputeTicks(candidate.Position, threatened.Position);
            if (travelTicks > ticksRemaining)
            {
                continue;
            }

            if (IsLargerSource(candidate, source))
            {
                source = candidate;
            }
        }

        return source is null
            ? BrainDecision.None
            : BrainDecision.Send(new SendArmyCommand(Player, source.Id, threatened.Id, ClampedSendSize(source.GarrisonCount)));
    }

    /// <summary>
    /// Clause 2 (D-31): a saturated base's production has already stopped earning, so spend its
    /// surplus on a level instead. A base is a candidate only when it is owned by this player, its
    /// garrison is at or above its cap (a tower's cap is empty, so a tower never qualifies - the
    /// empty case, never a sentinel comparison), it is not already under construction, its level is
    /// below <see cref="Base.MaxUpgradableLevel"/>, its garrison covers <see cref="Base.UpgradeCost"/>,
    /// and no enemy army is in flight to it (the cost is paid immediately while the level lands
    /// 100+ ticks later, D-30, so upgrading under attack can hand over a capture it would have
    /// held). Among candidates, upgrades the safest: the one whose nearest not-owned base is
    /// furthest away, ties broken by lowest id - the consolidate clause's front distance read the
    /// other way round (one distance rule, two clauses).
    /// </summary>
    private BrainDecision TryUpgrade(Match match, List<Base> ownBases)
    {
        Base? best = null;
        var bestDistance = -1.0;

        foreach (var candidate in ownBases)
        {
            if (!IsUpgradeCandidate(match, candidate))
            {
                continue;
            }

            var nearestNotOwnedDistance = NearestNotOwnedDistance(match, candidate);

            // ownBases is ascending by id, so a strictly-greater distance is the only way to
            // replace the current pick - an equal distance leaves the lower id in place.
            if (nearestNotOwnedDistance > bestDistance)
            {
                best = candidate;
                bestDistance = nearestNotOwnedDistance;
            }
        }

        return best is null
            ? BrainDecision.None
            : BrainDecision.Upgrading(new UpgradeCommand(Player, best.Id));
    }

    private bool IsUpgradeCandidate(Match match, Base candidate)
    {
        var cap = candidate.GarrisonCap;
        if (cap is null || candidate.GarrisonCount < cap.Value)
        {
            return false;
        }

        if (candidate.Construction is not null)
        {
            return false;
        }

        if (candidate.Level >= candidate.MaxUpgradableLevel)
        {
            return false;
        }

        if (candidate.GarrisonCount < candidate.UpgradeCost)
        {
            return false;
        }

        return !AnyEnemyArmyInFlightTo(match, candidate.Id);
    }

    private bool AnyEnemyArmyInFlightTo(Match match, int baseId)
    {
        var armies = match.ArmiesInFlight;
        for (var i = 0; i < armies.Count; i++)
        {
            if (armies[i].TargetBaseId == baseId && armies[i].Owner != Player)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The distance from <paramref name="from"/> to the nearest base this player does not own -
    /// shared by the consolidate clause (nearest is the front) and the upgrade clause (furthest is
    /// safest), per D-31: one distance rule, not two.
    /// </summary>
    private double NearestNotOwnedDistance(Match match, Base from)
    {
        var allBases = match.Bases;
        var nearest = double.MaxValue;

        for (var i = 0; i < allBases.Count; i++)
        {
            if (allBases[i].Owner == Player)
            {
                continue;
            }

            var distance = Distance(from.Position, allBases[i].Position);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Clause 3 (FR-7): with an owned Producer saturated past <see cref="LevelTable.ConversionCost"/>
    /// and nothing to defend or upgrade, convert the front - the own base closest to any base it does
    /// not own, the same distance rule <see cref="TryConsolidate"/> uses to find its front - to a
    /// tower. Skipped when the AI owns fewer than two bases (converting its only base would remove
    /// its sole source of new units) or when no candidate qualifies (see
    /// <see cref="IsConvertCandidate"/>: owned, a Producer, not under construction, garrison at or
    /// above the conversion cost, and no enemy army in flight to it - the same threatened-base guard
    /// <see cref="IsUpgradeCandidate"/> already uses, since the cost is paid immediately while the
    /// type change lands 100 ticks later, D-30).
    /// </summary>
    private BrainDecision TryConvert(Match match, List<Base> ownBases)
    {
        if (ownBases.Count < 2)
        {
            return BrainDecision.None;
        }

        Base? best = null;
        var bestDistance = double.MaxValue;

        foreach (var candidate in ownBases)
        {
            if (!IsConvertCandidate(match, candidate))
            {
                continue;
            }

            var nearestNotOwnedDistance = NearestNotOwnedDistance(match, candidate);

            // ownBases is ascending by id, so a strictly-smaller distance is the only way to
            // replace the current pick - an equal distance leaves the lower id in place, the same
            // tie-break TryConsolidate's front search uses.
            if (nearestNotOwnedDistance < bestDistance)
            {
                best = candidate;
                bestDistance = nearestNotOwnedDistance;
            }
        }

        return best is null
            ? BrainDecision.None
            : BrainDecision.Converting(new ConvertCommand(Player, best.Id, BaseType.Tower));
    }

    private bool IsConvertCandidate(Match match, Base candidate)
    {
        if (candidate.Type != BaseType.Producer)
        {
            return false;
        }

        if (candidate.Construction is not null)
        {
            return false;
        }

        if (candidate.GarrisonCount < LevelTable.ConversionCost)
        {
            return false;
        }

        return !AnyEnemyArmyInFlightTo(match, candidate.Id);
    }

    /// <summary>
    /// Clause 4 (FR-7): considering own bases in descending garrison order, and for each the bases
    /// it does not own in ascending distance order, among the winnable, untargeted candidates prefer
    /// the one with the lowest <see cref="TotalExpectedTowerLoss"/> - a preference, not a refusal:
    /// the AI still attacks the only winnable target even when it crosses an enemy tower's range.
    /// Winnable means <c>floor(sourceGarrison * 50 / 100)</c> - unclamped, so a source with 0 or 1
    /// garrison can never be winnable, unlike the clamped-to-1 size <see cref="SendStrengthCalculator"/>
    /// computes for the send itself - minus the expected tower loss, strictly exceeds the target's
    /// garrison predicted at arrival.
    /// </summary>
    private BrainDecision TryAttack(Match match, List<Base> ownBases)
    {
        var currentTick = match.ElapsedTicks;

        var sources = new List<Base>(ownBases);
        sources.Sort((a, b) =>
        {
            var byGarrisonDescending = b.GarrisonCount.CompareTo(a.GarrisonCount);
            return byGarrisonDescending != 0 ? byGarrisonDescending : a.Id.CompareTo(b.Id);
        });

        var allBases = match.Bases;

        foreach (var source in sources)
        {
            var targets = new List<Base>();
            for (var i = 0; i < allBases.Count; i++)
            {
                if (allBases[i].Owner != Player)
                {
                    targets.Add(allBases[i]);
                }
            }

            targets.Sort((a, b) =>
            {
                var byDistance = Distance(source.Position, a.Position).CompareTo(Distance(source.Position, b.Position));
                return byDistance != 0 ? byDistance : a.Id.CompareTo(b.Id);
            });

            // Unclamped: a source with 0 or 1 garrison must stay unwinnable, unlike the clamped-to-1
            // size SendStrengthCalculator computes for the eventual send.
            var unclampedHalfGarrison = source.GarrisonCount * (int)SendStrength.Half / 100;

            Base? bestTarget = null;
            var bestExpectedTowerLoss = int.MaxValue;

            foreach (var target in targets)
            {
                if (AlreadyTargetedByOwnArmy(match, target.Id))
                {
                    continue;
                }

                var travelTicks = TravelTimeCalculator.ComputeTicks(source.Position, target.Position);
                var arrivalTick = currentTick + travelTicks;
                var predictedGarrison = PredictGarrison(target, currentTick, arrivalTick);
                var expectedTowerLoss = TotalExpectedTowerLoss(match, source.Position, target.Position);
                var attackingUnitCount = unclampedHalfGarrison - expectedTowerLoss;

                if (attackingUnitCount > predictedGarrison && expectedTowerLoss < bestExpectedTowerLoss)
                {
                    bestTarget = target;
                    bestExpectedTowerLoss = expectedTowerLoss;
                }
            }

            if (bestTarget is not null)
            {
                var unitCount = SendStrengthCalculator.Compute(source.GarrisonCount, SendStrength.Half);
                return BrainDecision.Send(new SendArmyCommand(Player, source.Id, bestTarget.Id, unitCount));
            }
        }

        return BrainDecision.None;
    }

    /// <summary>
    /// The sum, over every enemy-owned tower whose range the <paramref name="source"/>-to-
    /// <paramref name="target"/> segment crosses, of <see cref="TowerThreatEstimator"/>'s estimated
    /// units lost - never the AI's own towers, since a player's armies fly through their own towers
    /// untouched (FR-4).
    /// </summary>
    private int TotalExpectedTowerLoss(Match match, MapPoint source, MapPoint target)
    {
        var total = 0;
        var allBases = match.Bases;

        for (var i = 0; i < allBases.Count; i++)
        {
            var candidate = allBases[i];
            if (candidate.Type != BaseType.Tower || candidate.Owner is null || candidate.Owner == Player)
            {
                continue;
            }

            total += TowerThreatEstimator.EstimateUnitsLost(source, target, candidate.Position, candidate.Level);
        }

        return total;
    }

    /// <summary>
    /// Clause 5: with nothing to defend, upgrade, convert, or win, feed the front - the own base
    /// closest to any base it does not own - from the largest other own base. Skipped when the AI
    /// owns fewer than two bases, or when the front already has an AI army in flight to it.
    /// </summary>
    private BrainDecision TryConsolidate(Match match, List<Base> ownBases)
    {
        if (ownBases.Count < 2)
        {
            return BrainDecision.None;
        }

        Base? front = null;
        var frontDistance = double.MaxValue;

        foreach (var candidate in ownBases)
        {
            var nearestNotOwnedDistance = NearestNotOwnedDistance(match, candidate);

            // ownBases is ascending by id, so a strictly-smaller distance is the only way to
            // replace the current front - an equal distance leaves the lower id in place.
            if (nearestNotOwnedDistance < frontDistance)
            {
                front = candidate;
                frontDistance = nearestNotOwnedDistance;
            }
        }

        if (front is null || AlreadyTargetedByOwnArmy(match, front.Id))
        {
            return BrainDecision.None;
        }

        Base? source = null;
        foreach (var candidate in ownBases)
        {
            if (candidate.Id != front.Id && candidate.GarrisonCount > 0 && IsLargerSource(candidate, source))
            {
                source = candidate;
            }
        }

        return source is null
            ? BrainDecision.None
            : BrainDecision.Send(new SendArmyCommand(Player, source.Id, front.Id, ClampedSendSize(source.GarrisonCount)));
    }

    private List<Base> CollectOwnBasesAscendingById(Match match)
    {
        var result = new List<Base>();
        var bases = match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Owner == Player)
            {
                result.Add(bases[i]);
            }
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>
    /// True when the enemy armies already in flight to <paramref name="candidate"/> total at
    /// least its garrison predicted at the earliest of their arrival ticks.
    /// </summary>
    private bool TryGetThreatenedEarliestArrival(Match match, Base candidate, long currentTick, out long earliestArrival)
    {
        earliestArrival = long.MaxValue;
        var enemyUnitTotal = 0;
        var threatened = false;

        var armies = match.ArmiesInFlight;
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            if (army.TargetBaseId != candidate.Id || army.Owner == Player)
            {
                continue;
            }

            threatened = true;
            enemyUnitTotal += army.UnitCount;
            if (army.ArrivalTick < earliestArrival)
            {
                earliestArrival = army.ArrivalTick;
            }
        }

        if (!threatened)
        {
            return false;
        }

        var predictedGarrison = PredictGarrison(candidate, currentTick, earliestArrival);
        return enemyUnitTotal >= predictedGarrison;
    }

    private bool AlreadyTargetedByOwnArmy(Match match, int baseId)
    {
        var armies = match.ArmiesInFlight;
        for (var i = 0; i < armies.Count; i++)
        {
            if (armies[i].TargetBaseId == baseId && armies[i].Owner == Player)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A base's garrison as of <paramref name="futureTick"/> if nothing else changes: unowned bases
    /// and towers (D-24) never produce, so only an owned producer's count grows - through
    /// <see cref="ProductionCalculator"/>, the same arithmetic <see cref="Match"/> itself applies,
    /// rather than a second copy of it. Sharing it is what keeps the prediction honest about the
    /// garrison cap: extrapolating past a base's ceiling would have the AI credit a defender with
    /// units it can never have, and refuse attacks it would actually win.
    /// </summary>
    private static int PredictGarrison(Base b, long currentTick, long futureTick)
    {
        if (b.Owner is null || b.Type == BaseType.Tower)
        {
            return b.GarrisonCount;
        }

        return ProductionCalculator
            .Advance(new ProductionState(b.GarrisonCount, b.ProductionProgressTicks), b.Level, futureTick - currentTick)
            .GarrisonCount;
    }

    private static int ClampedSendSize(int garrison) => SendStrengthCalculator.Compute(garrison, SendStrength.Half);

    private static bool IsLargerSource(Base candidate, Base? current) =>
        current is null
        || candidate.GarrisonCount > current.GarrisonCount
        || (candidate.GarrisonCount == current.GarrisonCount && candidate.Id < current.Id);

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
