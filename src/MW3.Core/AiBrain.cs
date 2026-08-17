namespace MW3.Core;

/// <summary>
/// The AI opponent's brain (D-16, FR-6, FR-7): five clauses evaluated in priority order - defend,
/// upgrade, convert, attack, consolidate - the first that produces a command wins. Every send is
/// <see cref="SendStrengthCalculator.Compute"/> at <see cref="SendStrength.Half"/> (FR-1),
/// identical to the human's rule, so the AI can express nothing a human could not. No lookahead
/// beyond one decision and no randomness
/// (D-15): every clause is a fresh, deterministic read of the match as it stands right now.
/// <para>
/// Phase 6 FR-6 (<b>G-21</b>) adds a forge rule to clause 1 (a threatened forge outranks any
/// non-forge) and to clause 3 (a forge is built before a tower whenever one is owed). No source
/// describes how MW2's AI plays, so both rules - like every other heuristic in this class - are
/// MW3's own original design, never a port of MW2's AI.
/// </para>
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
    /// <para>
    /// Phase 6 FR-6 (G-21): a threatened <see cref="BaseType.Forge"/> is selected ahead of any
    /// threatened non-forge, whatever their ids - a forge's loss weakens its owner everywhere on
    /// the map, not just locally, and costs <see cref="MoraleTable"/>'s forge capture-loss on top.
    /// Among forges, and among non-forges, the lowest-id order is unchanged.
    /// </para>
    /// </summary>
    private BrainDecision TryDefend(Match match, List<Base> ownBases)
    {
        var currentTick = match.ElapsedTicks;

        // ownBases is ascending by id, so the first threatened candidate found in each group (forge,
        // non-forge) is that group's lowest id - null-coalescing below never overwrites it.
        Base? threatenedForge = null;
        var forgeEarliestArrival = 0L;
        Base? threatenedOther = null;
        var otherEarliestArrival = 0L;

        foreach (var candidate in ownBases)
        {
            if (!TryGetThreatenedEarliestArrival(match, candidate, currentTick, out var candidateEarliestArrival))
            {
                continue;
            }

            if (candidate.Type == BaseType.Forge)
            {
                if (threatenedForge is null)
                {
                    threatenedForge = candidate;
                    forgeEarliestArrival = candidateEarliestArrival;
                }
            }
            else if (threatenedOther is null)
            {
                threatenedOther = candidate;
                otherEarliestArrival = candidateEarliestArrival;
            }
        }

        var threatened = threatenedForge ?? threatenedOther;
        var earliestArrival = threatenedForge is not null ? forgeEarliestArrival : otherEarliestArrival;

        if (threatened is null || AlreadyTargetedByOwnArmy(match, threatened.Id))
        {
            return BrainDecision.None;
        }

        var ticksRemaining = earliestArrival - currentTick;
        Base? source = null;

        // The AI's own morale at prediction time (FR-4) - it is the sender of this prospective
        // reinforcement, so this is the speed Match.Execute would actually lock in were this
        // command submitted right now. Constant across every candidate below - the AI's morale
        // cannot change mid-loop.
        var speed = Match.EffectiveArmySpeedUnitsPerTick(match.MoraleFor(Player).Level);

        foreach (var candidate in ownBases)
        {
            if (candidate.Id == threatened.Id || candidate.GarrisonCount <= 0)
            {
                continue;
            }

            var path = PathCalculator.ComputePath(candidate.Position, threatened.Position, match.Obstacles);
            var travelTicks = TravelTimeCalculator.ComputeTicks(path.Length, speed);
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
    /// shared by the consolidate clause (nearest is the front), the upgrade clause (furthest is
    /// safest) and clause 3's forge and tower branches, per D-31: one distance rule, not four.
    /// <para>
    /// Phase 7 FR-6: the distance measured is the <b>route</b> length
    /// (<see cref="PathCalculator.ComputePath"/> against the match's obstacles), not the straight
    /// line between the two positions - §5's "never measure a journey in straight-line distance
    /// again". On a map with no obstacles the route is the two-waypoint straight path and its length
    /// is computed identically, so this changes nothing on Small or Big; on Medium an enemy base
    /// directly across the obstacle stops counting as near. This changes what the one rule measures,
    /// not how many rules there are.
    /// </para>
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

            var distance = PathCalculator.ComputePath(from.Position, allBases[i].Position, match.Obstacles).Length;
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Clause 3 (FR-7, phase 6 FR-6/G-21): with nothing to defend or upgrade, first checks whether a
    /// forge is owed - <see cref="Match.ForgeCountFor"/> below <c>producerCount / ForgeTable.ProducersPerForge</c>
    /// (integer division, MW2's own published ratio, <c>MW2-RULES.md</c> §2.4), where
    /// <c>producerCount</c> is how many of <paramref name="ownBases"/> are still
    /// <see cref="BaseType.Producer"/> - and if so converts the rear-most candidate to a forge via
    /// <see cref="TryConvertToForge"/>, without ever falling through to a tower conversion on that
    /// same decision. A forge is never owed twice for the same producers: converting one drops
    /// <c>producerCount</c> by one and raises the forge count by one in the same command, so the same
    /// integer division cannot immediately re-trigger (no oscillation). Only when no forge is owed
    /// does this fall through to <see cref="TryConvertToTower"/>, unchanged since FR-7. Skipped
    /// entirely when the AI owns fewer than two bases (converting its only base would remove its
    /// sole source of new units).
    /// </summary>
    private BrainDecision TryConvert(Match match, List<Base> ownBases)
    {
        if (ownBases.Count < 2)
        {
            return BrainDecision.None;
        }

        var producerCount = 0;
        foreach (var candidate in ownBases)
        {
            if (candidate.Type == BaseType.Producer)
            {
                producerCount++;
            }
        }

        if (match.ForgeCountFor(Player) < producerCount / ForgeTable.ProducersPerForge)
        {
            return TryConvertToForge(match, ownBases);
        }

        return TryConvertToTower(match, ownBases);
    }

    /// <summary>
    /// Builds a forge at the rear-most convert candidate - the own base whose
    /// <see cref="NearestNotOwnedDistance"/> is <b>greatest</b>, ties broken by lowest id - the same
    /// distance rule <see cref="TryUpgrade"/> already uses for "safest" (D-31: one distance rule, now
    /// three readers). A forge produces nothing and fires at nothing, so nothing is lost by building
    /// it as far from any front as possible. Candidacy is <see cref="IsConvertCandidate"/>, unchanged
    /// - which already excludes an owned <see cref="BaseType.Forge"/>, since it requires
    /// <see cref="BaseType.Producer"/>.
    /// </summary>
    private BrainDecision TryConvertToForge(Match match, List<Base> ownBases)
    {
        Base? best = null;
        var bestDistance = -1.0;

        foreach (var candidate in ownBases)
        {
            if (!IsConvertCandidate(match, candidate))
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
            : BrainDecision.Converting(new ConvertCommand(Player, best.Id, BaseType.Forge));
    }

    /// <summary>
    /// Converts the front - the own base closest to any base it does not own, the same distance rule
    /// <see cref="TryConsolidate"/> uses to find its front - to a tower. Unchanged since FR-7: yields
    /// nothing when no candidate qualifies (see <see cref="IsConvertCandidate"/>: owned, a Producer,
    /// not under construction, garrison at or above the conversion cost, and no enemy army in flight
    /// to it - the same threatened-base guard <see cref="IsUpgradeCandidate"/> already uses, since
    /// the cost is paid immediately while the type change lands 100 ticks later, D-30).
    /// </summary>
    private BrainDecision TryConvertToTower(Match match, List<Base> ownBases)
    {
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
    /// Clause 4 (FR-7, FR-6): considering own bases in descending garrison order, and for each the
    /// bases it does not own in ascending <b>route</b> length order (phase 7 FR-6 - the length of the
    /// path the army would fly, precomputed once per target before the sort rather than measured
    /// inside the comparator), among the winnable, untargeted candidates
    /// prefer the one with the lowest <see cref="TotalExpectedTowerLoss"/> - a preference, not a
    /// refusal: the AI still attacks the only winnable target even when it crosses an enemy tower's
    /// range. Winnable means <c>floor(sourceGarrison * 50 / 100)</c> - unclamped, so a source with 0
    /// or 1 garrison can never be winnable, unlike the clamped-to-1 size
    /// <see cref="SendStrengthCalculator"/> computes for the send itself - minus the expected tower
    /// loss, would capture the target's garrison predicted at arrival
    /// (<see cref="CombatResolver.WouldCapture"/>, weighing the target's own
    /// <see cref="Base.DefencePercentage"/>), not merely outnumber it. Among candidates tied on the
    /// lowest expected tower loss (FR-6), prefer the highest predicted net morale swing - a second,
    /// separate comparison key, never blended into one score - computed from the same
    /// <see cref="MoraleTable"/> a real capture would use (<see cref="PredictedMoraleSwing"/>). Ties
    /// on both keys fall back to the existing route-length-ascending-then-id order (the order
    /// <paramref name="ownBases"/>/<c>targets</c> were already sorted in), unchanged in shape.
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

        // This player's own forge term (FR-3) is the same for every source and every target it
        // considers this decision - nothing inside the loops changes ownership - so it is derived
        // once rather than per candidate.
        var ownForgeAttackPercent = match.ForgeAttackPercentFor(Player);

        // The AI's own morale (FR-4) - it is the prospective sender of every candidate send below,
        // so this is the speed Match.Execute would lock in for any of them were it submitted right
        // now. Constant across both loops: nothing inside them changes the AI's morale.
        var speed = Match.EffectiveArmySpeedUnitsPerTick(match.MoraleFor(Player).Level);

        foreach (var source in sources)
        {
            // FR-6: one ComputePath per (source, target) pair, computed here and then reused as the
            // ordering key, for the arrival tick, and for the tower loss - never recomputed inside
            // the comparator, which would run it O(n log n) times per target.
            var targets = new List<TargetRoute>();
            for (var i = 0; i < allBases.Count; i++)
            {
                if (allBases[i].Owner != Player)
                {
                    var candidate = allBases[i];
                    targets.Add(new TargetRoute(
                        candidate,
                        PathCalculator.ComputePath(source.Position, candidate.Position, match.Obstacles)));
                }
            }

            targets.Sort((a, b) =>
            {
                var byRouteLength = a.Path.Length.CompareTo(b.Path.Length);
                return byRouteLength != 0 ? byRouteLength : a.Target.Id.CompareTo(b.Target.Id);
            });

            // Unclamped: a source with 0 or 1 garrison must stay unwinnable, unlike the clamped-to-1
            // size SendStrengthCalculator computes for the eventual send.
            var unclampedHalfGarrison = source.GarrisonCount * (int)SendStrength.Half / 100;

            Base? bestTarget = null;
            var bestExpectedTowerLoss = int.MaxValue;
            var bestMoraleSwing = int.MinValue;

            foreach (var candidate in targets)
            {
                var target = candidate.Target;
                if (AlreadyTargetedByOwnArmy(match, target.Id))
                {
                    continue;
                }

                var path = candidate.Path;
                var travelTicks = TravelTimeCalculator.ComputeTicks(path.Length, speed);
                var arrivalTick = currentTick + travelTicks;
                var predictedGarrison = PredictGarrison(target, currentTick, arrivalTick);
                var expectedTowerLoss = TotalExpectedTowerLoss(match, path, speed);
                var attackingUnitCount = unclampedHalfGarrison - expectedTowerLoss;

                // Mechanical, not judgement (FR-2, and FR-3 for the forge term): feeding the same
                // live morale and forge indices Resolve would use into the shared WouldCapture
                // predicate, so this prediction cannot silently disagree with what actually happens
                // - the disagreement #68 was filed to close, and the same hazard D-45 names for
                // forges.
                var attackerMoralePercent = MoraleTable.AttackPercentage(match.MoraleFor(Player).Level);
                var defenderMoralePercent = target.Owner is Player targetOwner
                    ? MoraleTable.DefencePercentage(match.MoraleFor(targetOwner).Level)
                    : 100;
                var attackerIndex = CombatResolver.ComposeAttackerIndex(attackerMoralePercent, ownForgeAttackPercent);
                var defenderIndex = CombatResolver.ComposeDefenderIndex(
                    target.DefencePercentage,
                    defenderMoralePercent,
                    match.ForgeDefencePercentFor(target.Owner));

                if (!CombatResolver.WouldCapture(attackerIndex, defenderIndex, attackingUnitCount, predictedGarrison))
                {
                    continue;
                }

                if (expectedTowerLoss > bestExpectedTowerLoss)
                {
                    continue;
                }

                // FR-6: among candidates tied on the lowest expected tower loss, prefer the one
                // predicted to net the most morale - a second, separate comparison key, not blended
                // into bestExpectedTowerLoss's own ordering.
                var moraleSwing = PredictedMoraleSwing(attackerIndex, defenderIndex, attackingUnitCount, predictedGarrison, expectedTowerLoss, target);

                if (expectedTowerLoss < bestExpectedTowerLoss || moraleSwing > bestMoraleSwing)
                {
                    bestTarget = target;
                    bestExpectedTowerLoss = expectedTowerLoss;
                    bestMoraleSwing = moraleSwing;
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
    /// The sum, over every tower whose range <paramref name="path"/> crosses and that is not the
    /// AI's own, of <see cref="TowerThreatEstimator"/>'s estimated units lost - a player's armies fly
    /// through their own towers untouched (FR-4). Includes an unowned tower (FR-2, D-47): the neutral
    /// tower fires at any player's army in range, so a route crossing it is a real threat and must
    /// not be scored as zero.
    /// <para>
    /// Phase 7 FR-6: <paramref name="path"/> is the route the evaluating clause already computed for
    /// its arrival prediction, so the cost of tower fire is charged along the segments the army would
    /// really fly rather than along a straight line between the two bases.
    /// </para>
    /// </summary>
    private int TotalExpectedTowerLoss(Match match, ArmyPath path, double speedUnitsPerTick)
    {
        var total = 0;
        var allBases = match.Bases;

        for (var i = 0; i < allBases.Count; i++)
        {
            var candidate = allBases[i];
            if (candidate.Type != BaseType.Tower || candidate.Owner == Player)
            {
                continue;
            }

            total += TowerThreatEstimator.EstimateUnitsLost(path, candidate.Position, candidate.Level, speedUnitsPerTick);
        }

        return total;
    }

    /// <summary>
    /// One candidate target of clause 4 paired with the single <see cref="ArmyPath"/> computed for it
    /// (FR-6). Exists so the path is computed once per (source, target) pair and then read three
    /// times - the sort key, the arrival tick, and the tower loss - instead of recomputed at each.
    /// </summary>
    private readonly struct TargetRoute
    {
        internal TargetRoute(Base target, ArmyPath path)
        {
            Target = target;
            Path = path;
        }

        internal Base Target { get; }

        internal ArmyPath Path { get; }
    }

    /// <summary>
    /// FR-6: the AI's predicted net morale change for capturing <paramref name="target"/> - not a
    /// veto, only a tiebreak among winnable candidates already tied on
    /// <see cref="TotalExpectedTowerLoss"/>. <c>MoraleTable.CaptureGain(target.Type, target.Level,
    /// wasOpponentOwned) - MoraleTable.AttackingUnitDiedLoss * predictedAttackerDeaths</c>, where
    /// <paramref name="target"/> is opponent-owned whenever it is not neutral (the two-player match,
    /// D-9) and <c>predictedAttackerDeaths</c> is D-41's Wu-minus-remaining rule -
    /// <paramref name="attackingUnitCount"/> minus the wave's own predicted
    /// <see cref="CombatResult.RemainingGarrison"/> - plus
    /// <paramref name="expectedTowerLoss"/>, since tower fire kills attacker units too and D-41
    /// costs every attacker death the same, wherever it happens. Read-only against
    /// <see cref="MoraleTable"/> and <see cref="CombatResolver"/> - no new literal morale number.
    /// </summary>
    private static int PredictedMoraleSwing(
        int attackerIndex,
        int defenderIndex,
        int attackingUnitCount,
        int predictedGarrison,
        int expectedTowerLoss,
        Base target)
    {
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, attackingUnitCount, predictedGarrison);
        var predictedAttackerDeaths = (attackingUnitCount - result.RemainingGarrison) + expectedTowerLoss;
        var wasOpponentOwned = target.Owner is not null;

        return MoraleTable.CaptureGain(target.Type, target.Level, wasOpponentOwned)
            - (MoraleTable.AttackingUnitDiedLoss * predictedAttackerDeaths);
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
    /// True when the enemy armies already in flight to <paramref name="candidate"/> would capture
    /// it (<see cref="CombatResolver.WouldCapture"/>, weighing the candidate's own
    /// <see cref="Base.DefencePercentage"/>) at its garrison predicted at the earliest of their
    /// arrival ticks.
    /// </summary>
    private bool TryGetThreatenedEarliestArrival(Match match, Base candidate, long currentTick, out long earliestArrival)
    {
        earliestArrival = long.MaxValue;
        var enemyUnitTotal = 0;
        var threatened = false;
        Player? attacker = null;

        var armies = match.ArmiesInFlight;
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            if (army.TargetBaseId != candidate.Id || army.Owner == Player)
            {
                continue;
            }

            threatened = true;
            attacker = army.Owner;
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

        // Mechanical, not judgement (FR-2, and FR-3 for the forge term): the same shared
        // WouldCapture predicate Resolve uses, fed the attacker's (the threatening enemy's) and this
        // base's own owner's live morale and forge indices, so the threat prediction cannot silently
        // disagree with what actually happens (D-45).
        var attackerMoralePercent = MoraleTable.AttackPercentage(match.MoraleFor(attacker!).Level); // attacker is set whenever threatened is true, checked above
        var defenderMoralePercent = candidate.Owner is Player candidateOwner
            ? MoraleTable.DefencePercentage(match.MoraleFor(candidateOwner).Level)
            : 100;
        var attackerIndex = CombatResolver.ComposeAttackerIndex(attackerMoralePercent, match.ForgeAttackPercentFor(attacker!));
        var defenderIndex = CombatResolver.ComposeDefenderIndex(
            candidate.DefencePercentage,
            defenderMoralePercent,
            match.ForgeDefencePercentFor(candidate.Owner));
        return CombatResolver.WouldCapture(attackerIndex, defenderIndex, enemyUnitTotal, predictedGarrison);
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
        if (b.Owner is null || b.Type != BaseType.Producer)
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
}
