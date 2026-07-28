namespace MW3.Core;

/// <summary>
/// The AI opponent's brain (D-16, FR-6): three clauses evaluated in priority order - defend,
/// attack, consolidate - the first that produces a command wins. Every send is
/// <c>floor(garrison / 2)</c> clamped to a minimum of 1, identical to the human's rule, so the AI
/// can express nothing a human could not. No lookahead beyond one decision and no randomness
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
    /// Clause 2: considering own bases in descending garrison order, and for each the bases it
    /// does not own in ascending distance order, send at the first winnable, untargeted candidate
    /// and stop. Winnable means <c>floor(sourceGarrison / 2)</c> - unclamped - strictly exceeds the
    /// target's garrison predicted at arrival.
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

            var unclampedHalf = source.GarrisonCount / 2;

            foreach (var target in targets)
            {
                if (AlreadyTargetedByOwnArmy(match, target.Id))
                {
                    continue;
                }

                var travelTicks = TravelTimeCalculator.ComputeTicks(source.Position, target.Position);
                var arrivalTick = currentTick + travelTicks;
                var predictedGarrison = PredictGarrison(target, currentTick, arrivalTick);

                if (unclampedHalf > predictedGarrison)
                {
                    return BrainDecision.Send(new SendArmyCommand(Player, source.Id, target.Id, Math.Max(1, unclampedHalf)));
                }
            }
        }

        return BrainDecision.None;
    }

    /// <summary>
    /// Clause 3: with nothing to defend or win, feed the front - the own base closest to any base
    /// it does not own - from the largest other own base. Skipped when the AI owns fewer than two
    /// bases, or when the front already has an AI army in flight to it.
    /// </summary>
    private BrainDecision TryConsolidate(Match match, List<Base> ownBases)
    {
        if (ownBases.Count < 2)
        {
            return BrainDecision.None;
        }

        var allBases = match.Bases;
        Base? front = null;
        var frontDistance = double.MaxValue;

        foreach (var candidate in ownBases)
        {
            var nearestEnemyDistance = double.MaxValue;
            for (var i = 0; i < allBases.Count; i++)
            {
                if (allBases[i].Owner == Player)
                {
                    continue;
                }

                var distance = Distance(candidate.Position, allBases[i].Position);
                if (distance < nearestEnemyDistance)
                {
                    nearestEnemyDistance = distance;
                }
            }

            // ownBases is ascending by id, so a strictly-smaller distance is the only way to
            // replace the current front - an equal distance leaves the lower id in place.
            if (nearestEnemyDistance < frontDistance)
            {
                front = candidate;
                frontDistance = nearestEnemyDistance;
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
    /// never produce, so only an owned base's count grows - through
    /// <see cref="ProductionCalculator"/>, the same arithmetic <see cref="Match"/> itself applies,
    /// rather than a second copy of it. Sharing it is what keeps the prediction honest about the
    /// garrison cap: extrapolating past a base's ceiling would have the AI credit a defender with
    /// units it can never have, and refuse attacks it would actually win.
    /// </summary>
    private static int PredictGarrison(Base b, long currentTick, long futureTick)
    {
        if (b.Owner is null)
        {
            return b.GarrisonCount;
        }

        return ProductionCalculator
            .Advance(new ProductionState(b.GarrisonCount, b.ProductionProgressTicks), b.Level, futureTick - currentTick)
            .GarrisonCount;
    }

    private static int ClampedSendSize(int garrison) => Math.Max(1, garrison / 2);

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
