namespace MW3.Core;

/// <summary>
/// The match aggregate: players, the hardcoded map, and production. State changes only through
/// <see cref="Advance"/> (D-12, D-13) - no wall-clock read, no randomness.
/// </summary>
public sealed class Match
{
    public const long TickDurationMilliseconds = 100;

    public const long ProductionPeriodTicks = 10;

    /// <summary>
    /// Distance in normalized map units (<see cref="MapPoint"/>) an army covers per tick. Tuned so
    /// the full map width (1.0) takes 5 seconds - 50 ticks at <see cref="TickDurationMilliseconds"/>.
    /// </summary>
    public const double ArmySpeedUnitsPerTick = 0.02;

    private readonly List<Base> _bases;
    private readonly List<Army> _armies = new();
    private long _elapsedTicks;
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

    public IReadOnlyList<Base> Bases => _bases;

    /// <summary>
    /// Armies currently in flight. Read-only view over <see cref="Match"/>'s internal state; an
    /// army is added only by <see cref="Execute"/> and removed only by <see cref="Advance"/>, in
    /// the same call that resolves its arrival.
    /// </summary>
    public IReadOnlyList<Army> ArmiesInFlight => _armies;

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
            LaunchTick: _elapsedTicks,
            ArrivalTick: _elapsedTicks + travelTicks));

        return SendArmyOutcome.Accepted;
    }

    /// <summary>
    /// Advances the match by <paramref name="ticks"/> whole ticks. Production and army arrivals are
    /// processed in strict chronological order - one segment per distinct arrival tick reached,
    /// production applied only across each segment's span - so the same starting state and the same
    /// commands at the same tick counts always yield the same result (D-12 determinism) regardless
    /// of how the total is split across calls. A flat per-call production diff would not do this: it
    /// would let a capture's timing relative to a call's boundaries change how many production
    /// periods the captured base is credited for.
    /// </summary>
    public void Advance(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Ticks cannot be negative.");
        }

        var targetElapsedTicks = _elapsedTicks + ticks;

        while (true)
        {
            var nextArrivalTick = EarliestArrivalTickUpTo(targetElapsedTicks);
            var segmentEnd = nextArrivalTick ?? targetElapsedTicks;

            ApplyProduction(_elapsedTicks, segmentEnd);
            _elapsedTicks = segmentEnd;

            if (nextArrivalTick is null)
            {
                return;
            }

            ResolveArrivalsAtTick(_elapsedTicks);
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

    private void ApplyProduction(long fromTick, long toTick)
    {
        var unitsToAdd = (toTick / ProductionPeriodTicks) - (fromTick / ProductionPeriodTicks);
        if (unitsToAdd == 0)
        {
            return;
        }

        foreach (var b in _bases)
        {
            if (b.Owner is not null)
            {
                b.GarrisonCount += (int)unitsToAdd;
            }
        }
    }

    /// <summary>
    /// Resolves every army whose arrival tick is exactly <paramref name="tick"/>, in ascending
    /// creation order (the order <see cref="Execute"/> was called) - a deterministic, documented
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
    /// Combat is 1:1 with no defender advantage: N attackers against M defenders leaves the base
    /// captured by the attacker holding N - M when N &gt; M, or held by the defender holding M - N
    /// (possibly zero) when N &lt;= M.
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
            return;
        }

        if (army.UnitCount > target.GarrisonCount)
        {
            target.GarrisonCount = army.UnitCount - target.GarrisonCount;
            target.Owner = army.Owner;
        }
        else
        {
            target.GarrisonCount -= army.UnitCount;
        }
    }

    private static long ComputeTravelTicks(MapPoint from, MapPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var ticks = (long)Math.Ceiling(distance / ArmySpeedUnitsPerTick);
        return Math.Max(1, ticks);
    }
}
