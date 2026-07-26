namespace MW3.Core;

/// <summary>
/// The match aggregate: players, the hardcoded map, and production. State changes only through
/// <see cref="Advance"/> (D-12, D-13) - no wall-clock read, no randomness.
/// </summary>
public sealed class Match
{
    public const long TickDurationMilliseconds = 100;

    public const long ProductionPeriodTicks = 10;

    private readonly List<Base> _bases;
    private long _elapsedTicks;

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
    /// Advances the match by <paramref name="ticks"/> whole ticks. Production is derived from the
    /// match's total elapsed ticks rather than accumulated per call, so splitting the same total
    /// across several calls always yields the same result (D-12 determinism).
    /// </summary>
    public void Advance(long ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Ticks cannot be negative.");
        }

        var unitsProducedSoFar = _elapsedTicks / ProductionPeriodTicks;
        _elapsedTicks += ticks;
        var unitsProducedNow = _elapsedTicks / ProductionPeriodTicks;
        var unitsToAdd = unitsProducedNow - unitsProducedSoFar;

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
}
