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

    /// <summary>
    /// <see cref="ArmySpeedUnitsPerTick"/> scaled by <paramref name="moraleLevel"/>'s unit-speed
    /// percentage (FR-4, <see cref="MoraleTable.UnitSpeedPercentage"/>) - the single shared helper
    /// every speed consumer goes through, so no call site multiplies the base constant inline
    /// (D-22). At morale 0 this is bit-identical to <see cref="ArmySpeedUnitsPerTick"/>.
    /// </summary>
    public static double EffectiveArmySpeedUnitsPerTick(int moraleLevel) =>
        ArmySpeedUnitsPerTick * MoraleTable.UnitSpeedPercentage(moraleLevel) / 100.0;

    // Every BaseType, in declaration order (D-48) - AvailableActions offers one Convert action per
    // entry here other than the base's own, so the button order is stable regardless of enum-value
    // ordinal changes elsewhere.
    private static readonly BaseType[] _convertibleTypes = { BaseType.Producer, BaseType.Tower, BaseType.Forge };

    private readonly List<Base> _bases;
    private readonly List<Army> _armies = new();
    private readonly List<PendingWave> _pendingWaves = new();
    private readonly MoraleState _humanMorale = new();
    private readonly MoraleState _aiMorale = new();
    private int _nextArmyId;
    private int _nextSendId;

    public Match()
        : this(MapLayout.Slots)
    {
    }

    /// <summary>
    /// Builds a match from an explicit layout (D-44) rather than the shipped <see cref="MapLayout"/>.
    /// A test can construct a layout containing a neutral forge - or any other slot combination - and
    /// prove FR-2's rules before the shipped map itself changes. The parameterless constructor
    /// delegates here with <see cref="MapLayout.Slots"/> so there is exactly one bases-building code
    /// path (D-44).
    /// </summary>
    public Match(IReadOnlyList<MapSlot> layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (layout.Count == 0)
        {
            throw new ArgumentException("A match's layout must contain at least one slot.", nameof(layout));
        }

        HumanPlayer = new Player(Id: 1, PlayerControllerKind.Human);
        AiPlayer = new Player(Id: 2, PlayerControllerKind.Ai);

        _bases = new List<Base>(layout.Count);
        for (var i = 0; i < layout.Count; i++)
        {
            var slot = layout[i];
            var owner = slot.Kind switch
            {
                MapSlotKind.HumanStart => HumanPlayer,
                MapSlotKind.AiStart => AiPlayer,
                _ => null,
            };

            _bases.Add(new Base(id: i, slot.Position, slot.StartingGarrison, owner, slot.Type, slot.Level));
        }
    }

    public Player HumanPlayer { get; }

    public Player AiPlayer { get; }

    /// <summary>The human player's morale (D-37). Read-only outside this class - only <see cref="Match"/> mutates it.</summary>
    public MoraleState HumanMorale => _humanMorale;

    /// <summary>The AI player's morale (D-37). Read-only outside this class - only <see cref="Match"/> mutates it.</summary>
    public MoraleState AiMorale => _aiMorale;

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
    /// Validates and applies a <see cref="SendArmyCommand"/>, splitting accepted sends of more than
    /// 8 units into successive waves (FR-3). A rejection leaves every base's garrison and owner, and
    /// every in-flight army and pending wave, exactly as it was.
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

        // Speed is read once, here, from the sender's morale at the submission tick, and baked into
        // every wave's precomputed ArrivalTick below - never re-read live or per-wave (D-39).
        var speed = EffectiveArmySpeedUnitsPerTick(MoraleOf(command.IssuingPlayer).Level);
        var travelTicks = ComputeTravelTicks(source.Position, target.Position, speed);
        var sendId = _nextSendId++;
        var waveCount = SendWaveCalculator.WaveCount(command.UnitCount);
        var submissionTick = ElapsedTicks;

        for (var waveIndex = 1; waveIndex <= waveCount; waveIndex++)
        {
            var unitsInWave = SendWaveCalculator.UnitsInWave(command.UnitCount, waveIndex);
            var launchTickOffset = SendWaveCalculator.LaunchTickOffset(waveIndex);
            var launchTick = submissionTick + launchTickOffset;
            var arrivalTick = launchTick + travelTicks;

            var army = new Army(
                _nextArmyId++,
                command.IssuingPlayer,
                command.SourceBaseId,
                command.TargetBaseId,
                unitsInWave,
                launchTick,
                arrivalTick,
                sendId,
                waveIndex,
                waveCount);

            if (waveIndex == 1)
            {
                // Wave 1 enters ArmiesInFlight immediately
                _armies.Add(army);
            }
            else
            {
                // Waves 2..N are held in pending queue until their launch tick
                _pendingWaves.Add(new PendingWave(army, launchTick));
            }
        }

        // Only an accepted send updates the sender's last-send tick (FR-3 reads this; this feature
        // only maintains it) - a rejected command never reaches here at all.
        MoraleOf(command.IssuingPlayer).LastSendTick = submissionTick;

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
    /// What <paramref name="player"/> can do to <paramref name="baseId"/> right now: one Upgrade
    /// action followed by one Convert action per <see cref="BaseType"/> other than the base's own, in
    /// <see cref="BaseType"/> declaration order (D-48) - always exactly three actions for an owned
    /// base now that <see cref="BaseType"/> has three members. Each is computed independently of the
    /// others - a level-4 base's Upgrade reads AlreadyAtMaxLevel while a Convert can still be live,
    /// since none share state but the base itself (FR-5). Costs read from <see cref="LevelTable"/>
    /// and never named by the caller (D-25). Returns an empty list for an unknown base or one
    /// <paramref name="player"/> does not own - the widget that renders this answer never learns why
    /// there is nothing to show, because there is nothing for it to compute either way.
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

        var actions = new List<BaseAction>(_convertibleTypes.Length) { BuildUpgradeAction(target) };
        foreach (var type in _convertibleTypes)
        {
            if (type != target.Type)
            {
                actions.Add(BuildConvertAction(target, type));
            }
        }

        return actions;
    }

    private static BaseAction BuildUpgradeAction(Base target)
    {
        if (target.Level >= target.MaxUpgradableLevel)
        {
            return new BaseAction(BaseActionKind.Upgrade, Cost: 0, BaseActionAvailability.AlreadyAtMaxLevel);
        }

        if (target.Construction is not null)
        {
            return new BaseAction(BaseActionKind.Upgrade, target.UpgradeCost, BaseActionAvailability.UnderConstruction);
        }

        var cost = target.UpgradeCost;
        var availability = target.GarrisonCount >= cost
            ? BaseActionAvailability.Affordable
            : BaseActionAvailability.GarrisonBelowCost;

        return new BaseAction(BaseActionKind.Upgrade, cost, availability);
    }

    private static BaseAction BuildConvertAction(Base target, BaseType targetType)
    {
        var cost = LevelTable.ConversionCost;

        if (target.Construction is not null)
        {
            return new BaseAction(BaseActionKind.Convert, cost, BaseActionAvailability.UnderConstruction, targetType);
        }

        var availability = target.GarrisonCount >= cost
            ? BaseActionAvailability.Affordable
            : BaseActionAvailability.GarrisonBelowCost;

        return new BaseAction(BaseActionKind.Convert, cost, availability, targetType);
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
            var segmentStart = ElapsedTicks;

            // Tower fire (FR-4) must be evaluated on every single tick, not skipped the way
            // production is computed in closed form over a span - but production itself must stay
            // closed-form regardless (a naive per-tick production call here was a real regression:
            // it silently abandoned batching for the rest of the match the moment any tower existed).
            // So while an owned tower exists, sweep every interior tick strictly before this
            // segment's boundary for fire only; the boundary tick's own fire check happens below,
            // after construction completion, exactly as the no-tower path already did.
            if (HasAnyOwnedTower())
            {
                for (var tick = segmentStart + 1; tick < segmentEnd; tick++)
                {
                    EvaluateTowerFireAtTick(tick);
                    EvaluateOutcome();

                    if (Outcome != MatchOutcome.InProgress)
                    {
                        ApplyProduction(segmentStart, tick);
                        ElapsedTicks = tick;
                        return;
                    }
                }
            }

            ApplyProduction(segmentStart, segmentEnd);
            ElapsedTicks = segmentEnd;

            if (nextBoundaryTick is null)
            {
                // Reached the requested total with no arrival or completion due: if a tower exists,
                // this final tick still needs its own fire check - the sweep above stops one short
                // of it on purpose, since a real boundary would still need construction completed
                // first, but there is no boundary here at all.
                if (HasAnyOwnedTower())
                {
                    EvaluateTowerFireAtTick(segmentEnd);
                    EvaluateOutcome();
                }

                return;
            }

            // Construction completion before wave launch before tower fire before arrivals
            // (D-30, FR-3c, D-24, D-35): a base finishing an upgrade or conversion on the tick it is
            // attacked defends at its new level or type; a conversion completing into a tower on this
            // exact tick still needs its own fire check. Pending waves launch after construction so a
            // base converted into a tower on this tick can fire at a just-launched wave. The call is
            // unconditional (safe and cheap when nothing is due at all) rather than gated, since it
            // only ever runs once per boundary, never once per tick.
            CompleteConstructionsAtTick(ElapsedTicks);
            LaunchPendingWavesAtTick(ElapsedTicks);
            EvaluateTowerFireAtTick(ElapsedTicks);
            ResolveArrivalsAtTick(ElapsedTicks);
            EvaluateOutcome();

            if (Outcome != MatchOutcome.InProgress)
            {
                return;
            }

            // Decay (FR-3) is ordered after tower fire and arrivals, and only runs while the match is
            // still in progress - nothing decays once Outcome leaves InProgress (phase 2 FR-7's
            // freeze), including on the very tick that decides it.
            EvaluateDecayAtTick(ElapsedTicks);
        }
    }

    /// <summary>
    /// Whether any base is a <see cref="BaseType.Tower"/> - the only condition under which anything
    /// can fire. No longer tests ownership (D-47, FR-2): a neutral tower fires from tick 0 on the
    /// shipped layout, so this guard stops being a real optimisation there and is only ever true for
    /// a layout that places no tower at all.
    /// </summary>
    private bool HasAnyOwnedTower()
    {
        foreach (var b in _bases)
        {
            if (b.Type == BaseType.Tower)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fires every tower that is ready on <paramref name="tick"/> (FR-4), owned or not (D-47, FR-2):
    /// each tracks its own <see cref="Base.LastFireTick"/> and is ready when that is null or at least
    /// its level's fire period has elapsed. A ready tower hits the closest army with a non-null owner
    /// within its range - ties broken by the lowest army id - removing exactly one unit from it; an
    /// army whose strength reaches zero is destroyed on the spot, never arriving. A garrison of zero
    /// does not stop a tower from firing (FR-3: a garrison is not ammunition). A tower never fires at
    /// its own owner's armies, and never at an army whose owner is null - no such army can exist in
    /// MW3 today (neutral bases never send), so that guard cannot yet be reached from a script; it is
    /// written ahead of its trigger so a later phase that gives neutrals a send does not silently
    /// acquire the wrong behaviour. Allocates nothing: plain indexed loops over both lists, no LINQ
    /// (docs/CONVENTIONS.md).
    /// </summary>
    private void EvaluateTowerFireAtTick(long tick)
    {
        for (var i = 0; i < _bases.Count; i++)
        {
            var tower = _bases[i];
            if (tower.Type != BaseType.Tower)
            {
                continue;
            }

            var towerOwner = tower.Owner;

            var period = LevelTable.Tower.FirePeriodTicks(tower.Level);
            if (tower.LastFireTick is long lastFire && tick - lastFire < period)
            {
                continue;
            }

            var range = LevelTable.Tower.RangeUnits(tower.Level);
            Army? nearest = null;
            var nearestDistance = double.MaxValue;

            for (var j = 0; j < _armies.Count; j++)
            {
                var army = _armies[j];
                if (army.Owner is null || army.Owner == towerOwner)
                {
                    continue;
                }

                var armyPosition = PositionAtTick(army, tick);
                var dx = armyPosition.X - tower.Position.X;
                var dy = armyPosition.Y - tower.Position.Y;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance > range)
                {
                    continue;
                }

                if (nearest is null || distance < nearestDistance || (distance == nearestDistance && army.Id < nearest.Id))
                {
                    nearest = army;
                    nearestDistance = distance;
                }
            }

            if (nearest is null)
            {
                continue;
            }

            tower.LastFireTick = tick;
            nearest.UnitCount--;

            // Tower fire destroys an attacking unit on identical terms to arrival combat (D-41):
            // the shot attacker may or may not survive the army outright, but the +10/-10 swing is
            // per shot either way. An unowned tower has nobody to award (D-47): the victim still
            // pays, but AwardMorale is skipped at the call site rather than given a null player.
            if (towerOwner is not null)
            {
                AwardMorale(towerOwner, MoraleTable.AttackingUnitDestroyedGain);
            }

            AwardMorale(nearest.Owner, -MoraleTable.AttackingUnitDiedLoss);

            if (nearest.UnitCount <= 0)
            {
                _armies.Remove(nearest);
            }
        }
    }

    /// <summary>
    /// An army's normalized-space position at <paramref name="tick"/> (FR-4): a pure function of its
    /// source and target base positions and its own launch/arrival ticks, recomputed fresh every
    /// time rather than accumulated - clamped to 0..1 so a tick outside its flight still resolves to
    /// an endpoint rather than extrapolating past it. Returns the source's own position (fraction 0)
    /// if either base is somehow unknown, which cannot happen for a live army on the hardcoded map.
    /// </summary>
    private MapPoint PositionAtTick(Army army, long tick)
    {
        var source = FindBase(army.SourceBaseId);
        var target = FindBase(army.TargetBaseId);
        if (source is null || target is null)
        {
            return default;
        }

        var span = army.ArrivalTick - army.LaunchTick;
        var fraction = span > 0 ? (double)(tick - army.LaunchTick) / span : 1.0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);

        var x = source.Position.X + ((target.Position.X - source.Position.X) * fraction);
        var y = source.Position.Y + ((target.Position.Y - source.Position.Y) * fraction);

        return new MapPoint(x, y);
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
    /// The earliest tick at or before <paramref name="upperBound"/> that either an army arrives, a
    /// construction completes, or a pending wave launches, or null if none is due yet. A completion
    /// tick and a wave launch tick are segment boundaries exactly like an arrival tick (D-30, FR-3c):
    /// production is computed in closed form across a segment (D-21a), so a period change mid-segment
    /// would otherwise be credited at the wrong rate for part of the span. Wave launching is a
    /// boundary so determinism is preserved across chunked Advance calls (D-12, D-14, D-15).
    /// </summary>
    private long? EarliestBoundaryTickUpTo(long upperBound)
    {
        var earliest = EarliestArrivalTickUpTo(upperBound);
        var completionTick = EarliestConstructionCompletionTickUpTo(upperBound);
        var waveLaunchTick = EarliestPendingWaveLaunchTickUpTo(upperBound);
        var decayTick = EarliestDecayTickUpTo(upperBound);

        if (completionTick is long c && (earliest is null || c < earliest))
        {
            earliest = c;
        }

        if (waveLaunchTick is long w && (earliest is null || w < earliest))
        {
            earliest = w;
        }

        if (decayTick is long d && (earliest is null || d < earliest))
        {
            earliest = d;
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
    /// The earliest pending wave's launch tick that is at or before <paramref name="upperBound"/>,
    /// or null if none is due yet (FR-3, D-35). Pending waves (D-33) wait outside
    /// <see cref="ArmiesInFlight"/> until this moment.
    /// </summary>
    private long? EarliestPendingWaveLaunchTickUpTo(long upperBound)
    {
        long? earliest = null;
        foreach (var pw in _pendingWaves)
        {
            if (pw.LaunchTick <= upperBound && (earliest is null || pw.LaunchTick < earliest))
            {
                earliest = pw.LaunchTick;
            }
        }

        return earliest;
    }

    /// <summary>
    /// The earliest tick at or before <paramref name="upperBound"/> on which either player's own
    /// decay cadence lands a period boundary (FR-3): a positive multiple of
    /// <see cref="MoraleTable.DecayPeriodTicks"/> ticks after that player's own last accepted send.
    /// Each player's cadence is independent, so this is the minimum of the two. Whether decay actually
    /// applies at that tick (the player may not yet be past their idle threshold) is decided by
    /// <see cref="EvaluateDecayAtTick"/>, not here - this only decides where <see cref="Advance"/>
    /// must stop to check.
    /// </summary>
    private long? EarliestDecayTickUpTo(long upperBound)
    {
        var humanTick = EarliestDecayTickForPlayerUpTo(_humanMorale, upperBound);
        var aiTick = EarliestDecayTickForPlayerUpTo(_aiMorale, upperBound);

        if (humanTick is null)
        {
            return aiTick;
        }

        if (aiTick is null)
        {
            return humanTick;
        }

        return Math.Min(humanTick.Value, aiTick.Value);
    }

    /// <summary>
    /// The earliest positive multiple of <see cref="MoraleTable.DecayPeriodTicks"/> after
    /// <paramref name="state"/>'s own last accepted send that is strictly after
    /// <see cref="ElapsedTicks"/> and at or before <paramref name="upperBound"/>, or null if none
    /// exists yet. A null <see cref="MoraleState.LastSendTick"/> is treated as the match's start tick
    /// (0) - a player who never sends is idle from the beginning, without needing a separate field to
    /// say so. No cursor is stored: <see cref="ElapsedTicks"/> (already-standing state) is enough to
    /// re-derive the next boundary on every call, which is what keeps decay free of the "next-decay-
    /// tick cache" this feature explicitly forbids (D-38).
    /// </summary>
    private long? EarliestDecayTickForPlayerUpTo(MoraleState state, long upperBound)
    {
        var lastSend = state.LastSendTick ?? 0;
        var sinceLastSend = ElapsedTicks - lastSend;
        var periodsElapsed = sinceLastSend / MoraleTable.DecayPeriodTicks;
        var nextBoundary = lastSend + ((periodsElapsed + 1) * MoraleTable.DecayPeriodTicks);

        return nextBoundary <= upperBound ? nextBoundary : null;
    }

    /// <summary>
    /// Applies inactivity decay for both players at <paramref name="tick"/> (FR-3), evaluated after
    /// tower fire and arrivals so a wave landing on this tick has already scored and decay applies to
    /// the post-combat total (D-38). A no-op for a player not yet past their own idle threshold, or
    /// already at <see cref="MoraleTable.PointFloor"/>.
    /// </summary>
    private void EvaluateDecayAtTick(long tick)
    {
        EvaluateDecayForPlayerAtTick(HumanPlayer, _humanMorale, tick);
        EvaluateDecayForPlayerAtTick(AiPlayer, _aiMorale, tick);
    }

    /// <summary>
    /// Decays one player's morale at <paramref name="tick"/> iff it is one of their own period
    /// boundaries and they have been idle at least their current level's threshold - both re-read from
    /// the level <paramref name="state"/> is at right now, so the bleed self-slows as it drops them
    /// (FR-3, D-38). The AI decays on identical terms to the human (S-8) - nothing here branches on
    /// <see cref="PlayerControllerKind"/>.
    /// </summary>
    private void EvaluateDecayForPlayerAtTick(Player player, MoraleState state, long tick)
    {
        var lastSend = state.LastSendTick ?? 0;
        var idleTicks = tick - lastSend;
        if (idleTicks <= 0 || idleTicks % MoraleTable.DecayPeriodTicks != 0)
        {
            // Not this player's own period boundary - the tick was reached for the other player's
            // cadence, or for an unrelated arrival/completion/wave-launch boundary.
            return;
        }

        var level = state.Level;
        if (idleTicks < MoraleTable.DecayThresholdTicks(level))
        {
            return;
        }

        AwardMorale(player, -MoraleTable.DecayPointsPerPeriod(level));
    }

    /// <summary>
    /// Launches every pending wave whose launch tick is exactly <paramref name="tick"/>,
    /// moving them into <see cref="ArmiesInFlight"/> (FR-3, D-35). These waves then become
    /// legitimate tower targets from this tick on (D-35). Once the outcome is decided, no pending
    /// wave ever launches, and the list is effectively frozen.
    /// </summary>
    private void LaunchPendingWavesAtTick(long tick)
    {
        if (Outcome != MatchOutcome.InProgress)
        {
            return;
        }

        List<PendingWave>? due = null;
        for (var i = 0; i < _pendingWaves.Count; i++)
        {
            var pw = _pendingWaves[i];
            if (pw.LaunchTick == tick)
            {
                (due ??= new List<PendingWave>()).Add(pw);
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (var pw in due)
        {
            _armies.Add(pw.Army);
            _pendingWaves.Remove(pw);
        }
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

                    // Lands at construction completion, not command acceptance (FR-1): a base
                    // captured mid-build already discarded its Construction on capture, so this
                    // case can only run for the owner who is still holding the base now.
                    if (b.Owner is Player owner)
                    {
                        AwardMorale(owner, MoraleTable.UpgradeGain(b.Type, upgrade.TargetLevel));
                    }

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
            if (b.Owner is null || b.Type != BaseType.Producer)
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

        // Read before any mutation below (FR-1): the defender at combat time, for both the
        // attacking-unit-death swing and (on capture) who is charged the capture-loss table.
        var defenderOwnerAtCombat = target.Owner;

        // The attacker's index is read live, at arrival (FR-2) - the sender's morale at the moment
        // the wave lands, not when the send was issued. No index is stored on Army. A neutral
        // defender (Owner is null, D-11) has no morale and composes at identity.
        var attackerMoralePercent = MoraleTable.AttackPercentage(MoraleOf(army.Owner).Level);
        var defenderMoralePercent = defenderOwnerAtCombat is Player defenderOwnerForIndex
            ? MoraleTable.DefencePercentage(MoraleOf(defenderOwnerForIndex).Level)
            : 100;

        var attackerIndex = CombatResolver.ComposeAttackerIndex(attackerMoralePercent);
        var defenderIndex = CombatResolver.ComposeDefenderIndex(target.DefencePercentage, defenderMoralePercent);
        var result = CombatResolver.Resolve(attackerIndex, defenderIndex, army.UnitCount, target.GarrisonCount);

        // Only attacking units generate morale, in both directions (D-41): Wu died on a failed
        // attack, Wu - remaining died on a successful capture (remaining becomes the new garrison).
        // The defender's own dead garrison (Bu) is worth nothing to either side.
        var attackerDeadCount = result.Captured ? army.UnitCount - result.RemainingGarrison : army.UnitCount;
        var deathSwing = attackerDeadCount > 0 ? attackerDeadCount * MoraleTable.AttackingUnitDestroyedGain : 0;

        // Netted per player rather than applied as two separate clamped writes (D-38): a capture and
        // its attacking-unit losses land on the same event, and clamping each delta independently
        // would let whichever one is applied first swallow the other at the ceiling or the floor.
        var capturerDelta = -deathSwing;
        var defenderDelta = deathSwing;

        if (result.Captured)
        {
            // The level and type this base held before capture-demotion applies (FR-1) - read here,
            // before either changes below.
            var levelAtCapture = target.Level;
            var typeAtCapture = target.Type;

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

            // Capture morale (FR-1): the capturer always scores - neutral scores less than an
            // opponent's base, but scores. Neutral has no previous owner, so nobody is charged the
            // loss table; an opponent's previous owner is (D-41, MW2-RULES.md §5.2, §5.3). Folded
            // into capturerDelta/defenderDelta rather than written immediately - see the netting
            // comment above.
            var wasOpponentOwned = defenderOwnerAtCombat is not null;
            capturerDelta += MoraleTable.CaptureGain(typeAtCapture, levelAtCapture, wasOpponentOwned);
            if (defenderOwnerAtCombat is Player previousOwner)
            {
                defenderDelta -= MoraleTable.CaptureLoss(typeAtCapture, levelAtCapture);
            }
        }
        else
        {
            target.GarrisonCount = result.RemainingGarrison;
        }

        // A single clamped write per player for this whole combat event (D-38) - see the netting
        // comment above capturerDelta/defenderDelta's declaration.
        if (capturerDelta != 0)
        {
            AwardMorale(army.Owner, capturerDelta);
        }

        if (defenderOwnerAtCombat is Player defender && defenderDelta != 0)
        {
            AwardMorale(defender, defenderDelta);
        }
    }

    /// <summary>
    /// <paramref name="player"/>'s live <see cref="MoraleState"/> (FR-2) - the same lookup
    /// <see cref="ResolveArrival"/> uses internally, exposed so <see cref="AiBrain"/>'s predictions
    /// can compose the identical morale term <see cref="CombatResolver.Resolve"/> will actually use, keeping the
    /// two from disagreeing (mirrors why <see cref="TravelTimeCalculator"/> is the one source of
    /// arrival timing for both).
    /// </summary>
    public MoraleState MoraleFor(Player player) => MoraleOf(player);

    private MoraleState MoraleOf(Player player) => player == HumanPlayer ? _humanMorale : _aiMorale;

    /// <summary>
    /// Applies a morale delta (positive or negative) to <paramref name="player"/>'s
    /// <see cref="MoraleState.Points"/>, clamped through <see cref="MoraleTable.ClampPoints"/>
    /// (D-38). The single mutation point every award/deduct site in this class goes through.
    /// </summary>
    private void AwardMorale(Player player, int delta)
    {
        var state = MoraleOf(player);
        state.Points = MoraleTable.ClampPoints(state.Points + delta);
    }

    private static long ComputeTravelTicks(MapPoint from, MapPoint to, double speedUnitsPerTick) =>
        TravelTimeCalculator.ComputeTicks(from, to, speedUnitsPerTick);

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
    /// flight or still pending launch. An army in flight might yet recapture a base, so elimination
    /// is never declared while one remains (FR-7) - and a pending wave (FR-3, D-35) is exactly such
    /// an army, merely not yet promoted into <see cref="ArmiesInFlight"/>; ignoring it would declare
    /// a sender eliminated while their own later waves could still turn the match around.
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

        foreach (var pendingWave in _pendingWaves)
        {
            if (pendingWave.Army.Owner == player)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A pending wave waiting to launch (FR-3, D-35). Waves 2..N of a multi-wave send wait in a
    /// private pending list until their <see cref="LaunchTick"/> is reached, at which point they
    /// enter <see cref="ArmiesInFlight"/> and become legitimate tower targets.
    /// </summary>
    private sealed class PendingWave
    {
        public PendingWave(Army army, long launchTick)
        {
            Army = army;
            LaunchTick = launchTick;
        }

        public Army Army { get; }

        public long LaunchTick { get; }
    }
}
