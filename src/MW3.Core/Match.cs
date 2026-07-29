namespace MW3.Core;

/// <summary>
/// The match aggregate: players, the hardcoded map, and production. State changes only through
/// <see cref="Advance"/> (D-12, D-13) - no wall-clock read, no randomness.
/// </summary>
public sealed class Match
{
    /// <summary>
    /// 50ms, the only tick duration MW2's five published village production rates land on exactly:
    /// each rate is <c>60 / level</c> ticks, all whole numbers (D-27, <c>docs/reference/MW2-PARITY.md</c>
    /// §3).
    /// </summary>
    public const long TickDurationMilliseconds = 50;

    /// <summary>
    /// Distance in normalized map units (<see cref="MapPoint"/>) an army covers per tick. Halved
    /// alongside the tick duration so the full map width (1.0) still takes 5 seconds - 100 ticks at
    /// <see cref="TickDurationMilliseconds"/> (D-27).
    /// </summary>
    public const double ArmySpeedUnitsPerTick = 0.01;

    private readonly List<Base> _bases;
    private readonly List<Army> _armies = new();
    private int _nextArmyId;

    public Match()
    {
        HumanPlayer = new Player(Id: 1, PlayerControllerKind.Human);
        AiPlayer = new Player(Id: 2, PlayerControllerKind.Ai);

        _bases = new List<Base>(MapLayout.Slots.Count);
        for (var i = 0; i < MapLayout.Slots.Count; i++)
        {
            var slot = MapLayout.Slots[i];
            var owner = slot.Kind switch
            {
                MapSlotKind.HumanStart => HumanPlayer,
                MapSlotKind.AiStart => AiPlayer,
                _ => null,
            };

            _bases.Add(new Base(id: i, slot.Position, slot.StartingGarrison, owner));
        }
    }

    public Player HumanPlayer { get; }

    public Player AiPlayer { get; }

    /// <summary>
    /// Ticks the match has advanced through so far. Read-only like all of <see cref="Match"/>'s
    /// other state (D-13); the AI brain (D-16) reads this alongside <see cref="Bases"/> and
    /// <see cref="ArmiesInFlight"/> to decide when a decision tick has been reached.
    /// </summary>
    public long ElapsedTicks { get; private set; }

    public IReadOnlyList<Base> Bases => _bases;

    /// <summary>
    /// Armies currently in flight. Read-only view over <see cref="Match"/>'s internal state; an
    /// army is added only by <see cref="Execute(SendArmyCommand)"/> and removed only by
    /// <see cref="Advance"/>, in
    /// the same call that resolves its arrival.
    /// </summary>
    public IReadOnlyList<Army> ArmiesInFlight => _armies;

    /// <summary>
    /// Whether the match is still undecided, or has been won or lost - read-only, changing only
    /// inside <see cref="Advance"/> (D-13, FR-7). Once decided, the simulation is frozen: further
    /// <see cref="Advance"/> calls change nothing and <see cref="Execute(SendArmyCommand)"/>,
    /// <see cref="Execute(UpgradeCommand)"/>, and <see cref="Execute(ConvertCommand)"/> reject every
    /// command.
    /// </summary>
    public MatchOutcome Outcome { get; private set; } = MatchOutcome.InProgress;

    /// <summary>
    /// Validates and applies a <see cref="SendArmyCommand"/>. A rejection leaves every base's
    /// garrison and owner, and every in-flight army, exactly as it was.
    /// </summary>
    public SendArmyOutcome Execute(SendArmyCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // A null issuer is a caller bug, not an ordinary rejection: without this it would compare
        // equal to a neutral base's absent owner and pass the ownership gate.
        if (command.IssuingPlayer is null)
        {
            throw new ArgumentException("The command's issuing player cannot be null.", nameof(command));
        }

        if (Outcome != MatchOutcome.InProgress)
        {
            return SendArmyOutcome.MatchAlreadyDecided;
        }

        var source = FindBase(command.SourceBaseId);
        var target = FindBase(command.TargetBaseId);
        if (source is null || target is null)
        {
            return SendArmyOutcome.BaseNotFound;
        }

        if (source.Owner != command.IssuingPlayer)
        {
            return SendArmyOutcome.SourceNotOwnedByIssuer;
        }

        if (command.SourceBaseId == command.TargetBaseId)
        {
            return SendArmyOutcome.SourceEqualsTarget;
        }

        if (command.UnitCount <= 0)
        {
            return SendArmyOutcome.UnitCountNotPositive;
        }

        if (command.UnitCount > source.GarrisonCount)
        {
            return SendArmyOutcome.UnitCountExceedsGarrison;
        }

        source.GarrisonCount -= command.UnitCount;

        var travelTicks = ComputeTravelTicks(source.Position, target.Position);
        _armies.Add(new Army(
            _nextArmyId++,
            command.IssuingPlayer,
            command.SourceBaseId,
            command.TargetBaseId,
            command.UnitCount,
            LaunchTick: ElapsedTicks,
            ArrivalTick: ElapsedTicks + travelTicks));

        return SendArmyOutcome.Accepted;
    }

    /// <summary>
    /// Validates and starts an <see cref="UpgradeCommand"/>, paying for the level out of the base's
    /// own garrison immediately. A rejection leaves every base exactly as it was. Spending down to a
    /// garrison of zero is deliberately legal: a base emptied by a send is already legal, stays
    /// owned, keeps producing, and can be taken by a single unit, and an upgrade is no different -
    /// the strongest economy, briefly undefended, is a gamble the rules allow rather than forbid.
    /// <see cref="Base.Level"/> does not rise until <see cref="Advance"/> reaches the completion tick
    /// (D-30, FR-3c) - the benefit is delayed, the cost is not.
    /// </summary>
    public UpgradeOutcome Execute(UpgradeCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // As on the send path: a null issuer would compare equal to a neutral base's absent owner
        // and slip past the ownership gate, leaving only the cost check between it and upgrading a
        // base nobody owns.
        if (command.IssuingPlayer is null)
        {
            throw new ArgumentException("The command's issuing player cannot be null.", nameof(command));
        }

        if (Outcome != MatchOutcome.InProgress)
        {
            return UpgradeOutcome.MatchAlreadyDecided;
        }

        var target = FindBase(command.BaseId);
        if (target is null)
        {
            return UpgradeOutcome.BaseNotFound;
        }

        if (target.Owner != command.IssuingPlayer)
        {
            return UpgradeOutcome.BaseNotOwnedByIssuer;
        }

        if (target.Level >= target.MaxUpgradableLevel)
        {
            return UpgradeOutcome.AlreadyAtMaxLevel;
        }

        if (target.Construction is not null)
        {
            return UpgradeOutcome.UnderConstruction;
        }

        var cost = target.UpgradeCost;
        if (target.GarrisonCount < cost)
        {
            return UpgradeOutcome.GarrisonBelowCost;
        }

        target.GarrisonCount -= cost;

        // Production progress is deliberately left alone - it carries into the new (shorter) period
        // once the upgrade completes, so upgrading at an awkward moment never silently burns ticks a
        // player cannot see.
        var duration = LevelTable.UpgradeBuildDurationTicks(target.Level);
        target.Construction = new PendingUpgrade(ElapsedTicks + duration, target.Level + 1);

        return UpgradeOutcome.Accepted;
    }

    /// <summary>
    /// Validates and starts a <see cref="ConvertCommand"/>, paying for it out of the base's own
    /// garrison immediately. A rejection leaves every base exactly as it was. The type does not
    /// change, and the level and production progress do not reset, until <see cref="Advance"/>
    /// reaches the completion tick (D-30, FR-3c) - until then the base keeps its previous type
    /// entirely, including for combat and production.
    /// </summary>
    public ConvertOutcome Execute(ConvertCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        // As on the send and upgrade paths: a null issuer would compare equal to a neutral base's
        // absent owner and slip past the ownership gate.
        if (command.IssuingPlayer is null)
        {
            throw new ArgumentException("The command's issuing player cannot be null.", nameof(command));
        }

        if (Outcome != MatchOutcome.InProgress)
        {
            return ConvertOutcome.MatchAlreadyDecided;
        }

        var target = FindBase(command.BaseId);
        if (target is null)
        {
            return ConvertOutcome.BaseNotFound;
        }

        if (target.Owner != command.IssuingPlayer)
        {
            return ConvertOutcome.BaseNotOwnedByIssuer;
        }

        if (target.Type == command.TargetType)
        {
            return ConvertOutcome.AlreadyOfTargetType;
        }

        if (target.Construction is not null)
        {
            return ConvertOutcome.UnderConstruction;
        }

        if (target.GarrisonCount < LevelTable.ConversionCost)
        {
            return ConvertOutcome.GarrisonBelowCost;
        }

        target.GarrisonCount -= LevelTable.ConversionCost;
        target.Construction = new PendingConversion(ElapsedTicks + LevelTable.ConversionBuildDurationTicks, command.TargetType);

        return ConvertOutcome.Accepted;
    }

    /// <summary>
    /// What <paramref name="player"/> can do to <paramref name="baseId"/> right now: exactly one
    /// <see cref="BaseAction"/> (Upgrade) this phase, its cost read from <see cref="LevelTable"/>
    /// and never named by the caller (D-25). Returns an empty list for an unknown base or one
    /// <paramref name="player"/> does not own - the widget that renders this answer never learns
    /// why there is nothing to show, because there is nothing for it to compute either way.
    /// </summary>
    public IReadOnlyList<BaseAction> AvailableActions(Player player, int baseId)
    {
        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        var target = FindBase(baseId);
        if (target is null || target.Owner != player)
        {
            return Array.Empty<BaseAction>();
        }

        if (target.Level >= target.MaxUpgradableLevel)
        {
            return new[] { new BaseAction(BaseActionKind.Upgrade, Cost: 0, BaseActionAvailability.AlreadyAtMaxLevel) };
        }

        if (target.Construction is not null)
        {
            return new[] { new BaseAction(BaseActionKind.Upgrade, target.UpgradeCost, BaseActionAvailability.UnderConstruction) };
        }

        var cost = target.UpgradeCost;
        var availability = target.GarrisonCount >= cost
            ? BaseActionAvailability.Affordable
            : BaseActionAvailability.GarrisonBelowCost;

        return new[] { new BaseAction(BaseActionKind.Upgrade, cost, availability) };
    }

    /// <summary>
    /// Advances the match by <paramref name="ticks"/> whole ticks. Production and army arrivals are
    /// processed in strict chronological order - one segment per distinct arrival tick reached,
    /// production applied only across each segment's span - so the same starting state and the same
    /// commands at the same tick counts always yield the same result (D-12 determinism) regardless
    /// of how the total is split across calls. A flat per-call production diff would not do this: it
    /// would let a capture's timing relative to a call's boundaries change how many production
    /// periods the captured base is credited for. Once <see cref="Outcome"/> is decided, this is a
    /// no-op - not an error - so the final board stays exactly as it was at the moment of decision
    /// (FR-7).
    /// </summary>
    public void Advance(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Ticks cannot be negative.");
        }

        if (Outcome != MatchOutcome.InProgress)
        {
            return;
        }

        var targetElapsedTicks = ElapsedTicks + ticks;

        while (true)
        {
            var nextBoundaryTick = EarliestBoundaryTickUpTo(targetElapsedTicks);
            var segmentEnd = nextBoundaryTick ?? targetElapsedTicks;

            ApplyProduction(ElapsedTicks, segmentEnd);
            ElapsedTicks = segmentEnd;

            if (nextBoundaryTick is null)
            {
                return;
            }

            // Construction completion before arrivals (D-30, FR-3c): a base finishing an upgrade or
            // conversion on the tick it is attacked defends at its new level or type.
            CompleteConstructionsAtTick(ElapsedTicks);
            ResolveArrivalsAtTick(ElapsedTicks);
            EvaluateOutcome();

            if (Outcome != MatchOutcome.InProgress)
            {
                return;
            }
        }
    }

    private Base? FindBase(int id)
    {
        foreach (var b in _bases)
        {
            if (b.Id == id)
            {
                return b;
            }
        }

        return null;
    }

    /// <summary>
    /// The earliest tick at or before <paramref name="upperBound"/> that either an army arrives or a
    /// construction completes, or null if neither is due yet. A completion tick is a segment boundary
    /// exactly like an arrival tick (D-30, FR-3c): production is computed in closed form across a
    /// segment (D-21a), so a period change mid-segment would otherwise be credited at the wrong rate
    /// for part of the span.
    /// </summary>
    private long? EarliestBoundaryTickUpTo(long upperBound)
    {
        var earliest = EarliestArrivalTickUpTo(upperBound);
        var completionTick = EarliestConstructionCompletionTickUpTo(upperBound);

        if (completionTick is long c && (earliest is null || c < earliest))
        {
            earliest = c;
        }

        return earliest;
    }

    /// <summary>
    /// The earliest in-flight army's arrival tick that is at or before <paramref name="upperBound"/>,
    /// or null if none is due yet. Every remaining army's arrival tick is already known to be after
    /// the current elapsed ticks, since one is resolved (and removed) the moment its tick is reached.
    /// </summary>
    private long? EarliestArrivalTickUpTo(long upperBound)
    {
        long? earliest = null;
        foreach (var army in _armies)
        {
            if (army.ArrivalTick <= upperBound && (earliest is null || army.ArrivalTick < earliest))
            {
                earliest = army.ArrivalTick;
            }
        }

        return earliest;
    }

    /// <summary>
    /// The earliest pending construction's completion tick that is at or before
    /// <paramref name="upperBound"/>, or null if none is due yet.
    /// </summary>
    private long? EarliestConstructionCompletionTickUpTo(long upperBound)
    {
        long? earliest = null;
        foreach (var b in _bases)
        {
            if (b.Construction is PendingConstruction pc && pc.CompletionTick <= upperBound && (earliest is null || pc.CompletionTick < earliest))
            {
                earliest = pc.CompletionTick;
            }
        }

        return earliest;
    }

    /// <summary>
    /// Completes every base whose construction's completion tick is exactly <paramref name="tick"/>
    /// (D-30, FR-3c): an upgrade raises the level by one, carrying production progress unchanged; a
    /// conversion sets the type, resets the level to the minimum, and zeroes progress in both
    /// directions - the same reset an instant conversion always applied.
    /// </summary>
    private void CompleteConstructionsAtTick(long tick)
    {
        foreach (var b in _bases)
        {
            if (b.Construction is not PendingConstruction pc || pc.CompletionTick != tick)
            {
                continue;
            }

            switch (pc)
            {
                case PendingUpgrade upgrade:
                    b.Level = upgrade.TargetLevel;
                    break;
                case PendingConversion conversion:
                    b.Type = conversion.TargetType;
                    b.Level = LevelTable.MinLevel;
                    b.ProductionProgressTicks = 0;
                    break;
            }

            b.Construction = null;
        }
    }

    /// <summary>
    /// Runs production for every owned base across one segment. Production is <em>per base</em>,
    /// not a single global count of periods crossed: each base carries its own progress toward its
    /// next unit, because levels give bases different production periods and a base at its cap
    /// stops accumulating while its neighbours keep going (D-21, D-22). Neutral bases never
    /// produce, so they never accumulate progress either - and neither does a tower (D-24), which
    /// holds and defends a garrison but never grows it; skipping it here rather than teaching
    /// <see cref="ProductionCalculator"/> about types is what keeps its progress at exactly zero on
    /// every tick, not merely frozen at whatever it held when converted.
    /// </summary>
    private void ApplyProduction(long fromTick, long toTick)
    {
        var spanTicks = toTick - fromTick;
        if (spanTicks <= 0)
        {
            return;
        }

        foreach (var b in _bases)
        {
            if (b.Owner is null || b.Type == BaseType.Tower)
            {
                continue;
            }

            var produced = ProductionCalculator.Advance(
                new ProductionState(b.GarrisonCount, b.ProductionProgressTicks),
                b.Level,
                spanTicks);

            b.GarrisonCount = produced.GarrisonCount;
            b.ProductionProgressTicks = produced.ProgressTicks;
        }
    }

    /// <summary>
    /// Resolves every army whose arrival tick is exactly <paramref name="tick"/>, in ascending
    /// creation order (the order <see cref="Execute(SendArmyCommand)"/> was called) - a
    /// deterministic, documented
    /// order so several arrivals at the same base on the same tick apply one at a time.
    /// </summary>
    private void ResolveArrivalsAtTick(long tick)
    {
        List<Army>? due = null;
        foreach (var army in _armies)
        {
            if (army.ArrivalTick == tick)
            {
                (due ??= new List<Army>()).Add(army);
            }
        }

        if (due is null)
        {
            return;
        }

        due.Sort((a, b) => a.Id.CompareTo(b.Id));

        foreach (var army in due)
        {
            ResolveArrival(army);
            _armies.Remove(army);
        }
    }

    /// <summary>
    /// Applies one army's arrival using the target's owner at this moment - not at launch - so a
    /// base that changed hands mid-flight is reinforced or attacked based on who holds it now.
    /// <para>
    /// <b>Superseded by D-29 (FR-3b):</b> an attack no longer resolves as plain 1:1 subtraction.
    /// Combat is <see cref="CombatResolver"/>'s <c>Bu = (a/d) × Wu</c>, so a defended base can hold
    /// against a wave that would have captured it under the old arithmetic. Reinforcement (this
    /// method's same-owner branch) is untouched - defence never applies to a player's own arriving
    /// units.
    /// </para>
    /// </summary>
    private void ResolveArrival(Army army)
    {
        var target = FindBase(army.TargetBaseId);
        if (target is null)
        {
            return;
        }

        if (target.Owner == army.Owner)
        {
            target.GarrisonCount += army.UnitCount;

            // Reaching the cap discards progress toward the next unit, whether the cap was reached
            // by producing or - as here - by reinforcement. Enforced at the write site rather than
            // left to the next Advance, so a base reinforced to its cap and drained again within
            // the same tick cannot smuggle banked progress through (D-21). A tower has no cap
            // (GarrisonCap is null) and never accumulates progress in the first place (D-24), so
            // there is nothing to discard for one.
            if (target.GarrisonCap is int cap && target.GarrisonCount >= cap)
            {
                target.ProductionProgressTicks = 0;
            }

            return;
        }

        var attackerIndex = CombatResolver.ComposeAttackerIndex();
        var defenderIndex = CombatResolver.ComposeDefenderIndex(target.DefencePercentage);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, army.UnitCount, target.GarrisonCount);

        if (result.Captured)
        {
            // The retake grace (D-30, FR-3c, MW2-RULES.md §2.5) is decided from the state this base
            // carried *before* this capture - read it before overwriting either field below. A true
            // retake is the capturing player equalling the owner this base had immediately before its
            // last change, within the grace window of that change; neutral -> human -> AI within the
            // window is not a retake, because the AI is not the owner the base had before the human
            // took it.
            var isRetakeWithinGrace =
                target.LastOwnerChangeTick is long lastChangeTick
                && ElapsedTicks - lastChangeTick <= LevelTable.RecaptureGraceTicks
                && army.Owner == target.OwnerBeforeLastChange;

            target.OwnerBeforeLastChange = target.Owner;
            target.LastOwnerChangeTick = ElapsedTicks;

            target.GarrisonCount = result.RemainingGarrison;
            target.Owner = army.Owner;

            // A build in progress belongs to the previous owner and does not transfer - discarded
            // outright, with no refund for the units already spent (D-30, FR-3c), the same as D-21a
            // already discards a previous owner's partial production progress.
            target.Construction = null;

            // The structure survives the fighting, but one level of the previous owner's
            // investment burns with it (D-23), floored at the minimum so an undeveloped base is
            // simply taken as-is - unless this capture is a retake within the grace window, which
            // skips the demotion (but restores nothing already lost, and does not interact with
            // conversion's own level reset). Progress toward the next unit belonged to the previous
            // owner and does not transfer: inheriting it would let the timing of a capture shift when
            // the new owner's first unit appears.
            if (!isRetakeWithinGrace && target.Level > LevelTable.MinLevel)
            {
                target.Level--;
            }

            target.ProductionProgressTicks = 0;
        }
        else
        {
            target.GarrisonCount = result.RemainingGarrison;
        }
    }

    private static long ComputeTravelTicks(MapPoint from, MapPoint to) => TravelTimeCalculator.ComputeTicks(from, to);

    /// <summary>
    /// Decides <see cref="Outcome"/> from the current elimination state, evaluated once per tick
    /// right after that tick's arrivals resolve - the only moment ownership or in-flight armies can
    /// change (FR-7). The human is checked first, so a same-tick double elimination resolves as
    /// defeat - arbitrary but fixed, since a tie has to break somehow and this is the simpler rule
    /// to state and test.
    /// </summary>
    private void EvaluateOutcome()
    {
        if (IsEliminated(HumanPlayer))
        {
            Outcome = MatchOutcome.HumanDefeat;
        }
        else if (IsEliminated(AiPlayer))
        {
            Outcome = MatchOutcome.HumanVictory;
        }
    }

    /// <summary>
    /// A player is eliminated only once irreversibly so: zero owned bases and zero armies still in
    /// flight. An army in flight might yet recapture a base, so elimination is never declared while
    /// one remains (FR-7).
    /// </summary>
    private bool IsEliminated(Player player)
    {
        foreach (var b in _bases)
        {
            if (b.Owner == player)
            {
                return false;
            }
        }

        foreach (var army in _armies)
        {
            if (army.Owner == player)
            {
                return false;
            }
        }

        return true;
    }
}
