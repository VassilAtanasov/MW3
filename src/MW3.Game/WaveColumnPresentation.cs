using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Pure geometry and timing arithmetic for FR-4's wave-column legibility (D-36) - decides nothing
/// and draws nothing, mirroring how <see cref="SendStrengthSelector"/> keeps its layout math
/// headlessly testable (D-25). <see cref="MatchScreen"/> owns every MonoGame type and every reused
/// buffer; this class only ever sees plain data.
/// </summary>
internal static class WaveColumnPresentation
{
    /// <summary>
    /// Wave <paramref name="waveIndex"/> (1-based) of <paramref name="waveCount"/>'s drawn radius,
    /// as a fraction of the viewport's smaller dimension - <paramref name="leadRadiusFraction"/> at
    /// wave 1, <paramref name="trailingRadiusFraction"/> at the last wave, linear in between. A
    /// single-wave send (<paramref name="waveCount"/> &lt;= 1) always returns
    /// <paramref name="leadRadiusFraction"/> unchanged, so an ordinary send draws bit-identically to
    /// before this feature.
    /// </summary>
    public static float RadiusFraction(int waveIndex, int waveCount, float leadRadiusFraction, float trailingRadiusFraction)
    {
        if (waveCount <= 1)
        {
            return leadRadiusFraction;
        }

        var t = (float)(waveIndex - 1) / (waveCount - 1);
        return leadRadiusFraction + ((trailingRadiusFraction - leadRadiusFraction) * t);
    }

    /// <summary>
    /// Fills <paramref name="output"/> with one (FromIndex, ToIndex) pair per spine segment - the
    /// indices into <paramref name="armiesInFlight"/> of two consecutive, currently-in-flight waves
    /// of the same send (grouped by <see cref="Army.SendId"/>, never by adjacency in the list, since
    /// a second send launched mid-column interleaves with the first). A send whose lead wave has
    /// already arrived and whose later waves are still pending draws a spine across only what is
    /// actually in <paramref name="armiesInFlight"/>, because a pending wave is simply absent from
    /// that list until its own launch tick (FR-3). Clears <paramref name="output"/> first and never
    /// allocates - callers reuse the same list every call, matching the no-per-frame-allocation rule
    /// (docs/CONVENTIONS.md) since this runs from <see cref="MatchScreen.Draw"/>.
    /// </summary>
    public static void ComputeSpineSegments(IReadOnlyList<Army> armiesInFlight, List<(int FromIndex, int ToIndex)> output)
    {
        ArgumentNullException.ThrowIfNull(armiesInFlight);
        ArgumentNullException.ThrowIfNull(output);

        output.Clear();

        for (var i = 0; i < armiesInFlight.Count; i++)
        {
            var current = armiesInFlight[i];
            var nextIndex = -1;
            var nextWaveIndex = int.MaxValue;

            for (var j = 0; j < armiesInFlight.Count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                var candidate = armiesInFlight[j];
                if (candidate.SendId != current.SendId || candidate.WaveIndex <= current.WaveIndex)
                {
                    continue;
                }

                if (candidate.WaveIndex < nextWaveIndex)
                {
                    nextWaveIndex = candidate.WaveIndex;
                    nextIndex = j;
                }
            }

            if (nextIndex >= 0)
            {
                output.Add((i, nextIndex));
            }
        }
    }

    /// <summary>
    /// Whether a presentation event (a tower's <see cref="Base.LastFireTick"/>, or the tick an
    /// in-flight army's <see cref="Army.UnitCount"/> was last observed to drop) should still read as
    /// a flash at <paramref name="elapsedTicks"/> - true while the event is within
    /// <paramref name="durationTicks"/> ticks in the past, false if it never happened
    /// (<paramref name="eventTick"/> is null) or has aged out.
    /// </summary>
    public static bool IsFlashing(long elapsedTicks, long? eventTick, int durationTicks) =>
        eventTick is long tick && elapsedTicks - tick < durationTicks;
}
