namespace MW3.Core;

/// <summary>
/// One player's morale: current points and the tick of their last accepted send (D-37). Mirrors
/// <see cref="ProductionState"/>'s role as small mutable per-subject simulation state, but is
/// per-player and global rather than per-base - <see cref="Match"/> owns exactly one per player and
/// is the only thing that mutates either field (D-13, D-37). <see cref="LastSendTick"/> is written by
/// this feature but read by nothing until FR-3's inactivity decay.
/// </summary>
public sealed class MoraleState
{
    internal MoraleState()
    {
        Points = 0;
        LastSendTick = null;
    }

    /// <summary>
    /// This player's morale points, always within <see cref="MoraleTable.PointFloor"/> and
    /// <see cref="MoraleTable.PointCeiling"/> inclusive (D-38). Every write goes through
    /// <see cref="MoraleTable.ClampPoints"/>.
    /// </summary>
    public int Points { get; internal set; }

    /// <summary>
    /// The tick this player's most recently accepted <see cref="SendArmyCommand"/> was submitted, or
    /// null if they have never sent one. Only an accepted send updates this (FR-3); a rejected
    /// command, an upgrade, and a convert all leave it untouched.
    /// </summary>
    public long? LastSendTick { get; internal set; }

    /// <summary>
    /// The 0-5 sun level <see cref="Points"/> derives to, never stored separately
    /// (<see cref="MoraleTable.LevelForPoints"/>, D-38).
    /// </summary>
    public int Level => MoraleTable.LevelForPoints(Points);
}
