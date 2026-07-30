namespace MW3.Core;

/// <summary>
/// An army in flight between two bases (D-12). Its position is a pure function of
/// <see cref="LaunchTick"/>, <see cref="ArrivalTick"/>, and the two bases it travels between -
/// recomputed each tick, never accumulated. Constructed only by <see cref="Match"/>; its
/// <see cref="UnitCount"/> - its current strength, which can now be lower than what it launched
/// with - changes only through <see cref="Match.Advance"/>, which removes it from
/// <see cref="Match.ArmiesInFlight"/> the moment that reaches zero (destroyed, never arriving) or
/// its arrival tick resolves (D-13). Capturing its source base still does not change its owner,
/// redirect it, or recall it - only an owned tower within range can reduce it (FR-4).
/// </summary>
public sealed class Army
{
    internal Army(int id, Player owner, int sourceBaseId, int targetBaseId, int unitCount, long launchTick, long arrivalTick, int sendId, int waveIndex, int waveCount)
    {
        Id = id;
        Owner = owner;
        SourceBaseId = sourceBaseId;
        TargetBaseId = targetBaseId;
        UnitCount = unitCount;
        LaunchTick = launchTick;
        ArrivalTick = arrivalTick;
        SendId = sendId;
        WaveIndex = waveIndex;
        WaveCount = waveCount;
    }

    public int Id { get; }

    public Player Owner { get; }

    public int SourceBaseId { get; }

    public int TargetBaseId { get; }

    /// <summary>
    /// This army's current strength - the count it launched with, minus any units a tower has shot
    /// down since (FR-4). Never negative; an army whose strength reaches zero is destroyed the same
    /// tick and never arrives.
    /// </summary>
    public int UnitCount { get; internal set; }

    public long LaunchTick { get; }

    public long ArrivalTick { get; }

    /// <summary>
    /// The send this army belongs to (FR-3). All waves from a single <see cref="Match.Execute(SendArmyCommand)"/>
    /// call share one send id, drawn from a <see cref="Match"/>-owned counter separate from the
    /// army-id counter.
    /// </summary>
    public int SendId { get; }

    /// <summary>
    /// This wave's 1-based index within its send (FR-3). A single-arrival send has <see cref="WaveIndex"/> = 1.
    /// </summary>
    public int WaveIndex { get; }

    /// <summary>
    /// The total number of waves in this send (FR-3). A single-arrival send has <see cref="WaveCount"/> = 1.
    /// </summary>
    public int WaveCount { get; }
}
