using MW3.Core;

namespace MW3.Server;

/// <summary>
/// Every number this feature adds, named once (D-22 - a number lives in the tuning table and a
/// constant, never inline at a call site). Mirrors <c>docs/game-server/REQUIREMENTS.md</c> §4
/// "Tuning values". The server tick period is <b>not</b> repeated here - <see cref="Match.TickDurationMilliseconds"/>
/// stays the one authority (D-62).
/// </summary>
internal static class ServerTuning
{
    /// <summary>Every 2 ticks (100 ms): how often a session's events and snapshot hash go out.</summary>
    internal const long SendIntervalTicks = 2;

    /// <summary>5 minutes with no connection attached, expressed in scheduler ticks.</summary>
    internal const long IdleEvictionTicks = 5 * 60 * 1000 / Match.TickDurationMilliseconds;

    /// <summary>10 seconds after a disconnect before the missing player's brain is substituted (D-65).</summary>
    internal const long DisconnectGraceTicks = 10 * 1000 / Match.TickDurationMilliseconds;

    /// <summary>The most concurrent sessions one process holds; a further <c>CreateSession</c> is refused with a reason.</summary>
    internal const int MaxConcurrentSessions = 64;
}
