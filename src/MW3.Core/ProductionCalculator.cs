namespace MW3.Core;

/// <summary>
/// Advances one base's production over a span of whole ticks. Producer-only - <see cref="Match"/>
/// never calls this for a tower (D-24), so it reads <see cref="LevelTable.Village"/> directly rather
/// than taking a <see cref="BaseType"/> it would never use for anything but that one ladder.
/// The single place this arithmetic lives: <see cref="Match"/> applies it, and <see cref="AiBrain"/>
/// predicts with it, so the AI can never disagree with the simulation about what a base will hold (a
/// divergence that in phase 2 would have made the AI refuse winnable attacks against capped bases).
/// <para>
/// Closed form rather than a tick-by-tick loop: a span can be thousands of ticks, and this runs per
/// owned base on every advance. Nothing here allocates.
/// </para>
/// </summary>
internal static class ProductionCalculator
{
    /// <summary>
    /// Production over <paramref name="spanTicks"/> for a base at <paramref name="level"/>.
    /// <para>
    /// At or above the cap, <em>nothing accumulates at all</em> - the state is returned untouched
    /// (D-21). That is what makes a base held at its cap and then drained take a full production
    /// period to produce again rather than popping a banked unit out immediately: reaching the cap
    /// leaves progress at exactly zero, because the tick that produced the capping unit consumed
    /// the progress that bought it, and every later tick spent at the cap is discarded.
    /// </para>
    /// <para>
    /// Chunking cannot change the result (D-12): below the cap the leftover ticks carry in
    /// <see cref="ProductionState.ProgressTicks"/>, and the cap is a clamp that lands on the same
    /// absolute state whether it is reached in one span or several.
    /// </para>
    /// </summary>
    internal static ProductionState Advance(ProductionState state, int level, long spanTicks)
    {
        var cap = LevelTable.Village.GarrisonCap(level);
        if (state.GarrisonCount >= cap)
        {
            // At or above the cap, progress is zero - not merely frozen at whatever it held. A base
            // that reaches its cap by *producing* lands on zero for free, but one pushed there by an
            // arrival would otherwise keep partial progress banked and then produce early once
            // drained, which is exactly the massing-on-a-staging-base path D-21 exists to allow.
            return state.ProgressTicks == 0 ? state : new ProductionState(state.GarrisonCount, progressTicks: 0);
        }

        if (spanTicks <= 0)
        {
            return state;
        }

        var period = LevelTable.Village.ProductionPeriodTicks(level);
        var availableTicks = state.ProgressTicks + spanTicks;
        var produced = availableTicks / period;
        var room = cap - state.GarrisonCount;

        if (produced >= room)
        {
            return new ProductionState(cap, progressTicks: 0);
        }

        return new ProductionState(state.GarrisonCount + (int)produced, availableTicks - (produced * period));
    }
}
