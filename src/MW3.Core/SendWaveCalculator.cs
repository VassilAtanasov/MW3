namespace MW3.Core;

/// <summary>
/// Wave splitting arithmetic for a multi-wave send (FR-3, parity G-2). A send of more than 8 units
/// splits into successive 8-unit waves launched at 5-tick intervals. The calculator holds both
/// constants and mirrors <see cref="SendStrengthCalculator"/>'s design: pure, engine-free, returning
/// scalars by index rather than a collection, so splitting allocates nothing beyond the
/// <see cref="Army"/> objects themselves.
/// </summary>
public static class SendWaveCalculator
{
    /// <summary>
    /// 8 units, MW2's published wave size (MW2-RULES.md §3.3 [S]).
    /// </summary>
    public const int WaveSizeUnits = 8;

    /// <summary>
    /// 5 ticks (250 ms), MW3's own number — MW2 publishes no interval (MW2-RULES.md §3.3, §10).
    /// Chosen above the fastest tower's 3-tick fire period so every wave gap admits a fresh shot at
    /// any tower level, and low enough that an ordinary 40-unit send finishes launching in 20 ticks
    /// (inside every travel edge on the map) while only a maxed 80-unit commitment stretches to 45.
    /// </summary>
    public const int WaveIntervalTicks = 5;

    /// <summary>
    /// The number of complete and partial waves a send of <paramref name="unitCount"/> units will
    /// split into: <c>ceil(unitCount / 8)</c>.
    /// </summary>
    public static int WaveCount(int unitCount) =>
        (unitCount + WaveSizeUnits - 1) / WaveSizeUnits;

    /// <summary>
    /// The number of units in wave <paramref name="waveIndex"/> (1-based) of a send of
    /// <paramref name="unitCount"/> units. Full waves return <see cref="WaveSizeUnits"/>; the
    /// final wave carries the remainder.
    /// </summary>
    public static int UnitsInWave(int unitCount, int waveIndex)
    {
        var unitsInFinalWave = unitCount % WaveSizeUnits;
        var waveCountValue = WaveCount(unitCount);

        if (waveIndex == waveCountValue && unitsInFinalWave > 0)
        {
            return unitsInFinalWave;
        }

        return WaveSizeUnits;
    }

    /// <summary>
    /// The tick offset of wave <paramref name="waveIndex"/> (1-based) from the send's submission
    /// tick: <c>(waveIndex - 1) × WaveIntervalTicks</c>. Wave 1 launches at tick 0; wave 2 at tick 5,
    /// etc.
    /// </summary>
    public static int LaunchTickOffset(int waveIndex) =>
        (waveIndex - 1) * WaveIntervalTicks;
}
