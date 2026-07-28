namespace MW3.Core;

/// <summary>
/// A base's production state: how many units it holds, and how many ticks it has accumulated toward
/// the next one. A struct so <see cref="ProductionCalculator"/> can return both together without
/// allocating - production runs for every owned base on every advance, which on the Android head is
/// every frame (REQUIREMENTS.md §5).
/// </summary>
internal readonly struct ProductionState
{
    internal ProductionState(int garrisonCount, long progressTicks)
    {
        GarrisonCount = garrisonCount;
        ProgressTicks = progressTicks;
    }

    internal int GarrisonCount { get; }

    internal long ProgressTicks { get; }
}
