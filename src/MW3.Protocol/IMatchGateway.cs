namespace MW3.Protocol;

/// <summary>
/// The seam between a renderer and whatever is running the match (D-57, D-74): read the current
/// <see cref="MatchSnapshot"/>, hand over elapsed wall-clock time, submit a
/// <see cref="GatewayCommand"/>. No member is typed in terms of the rules, which is what lets a
/// client compile without them.
///
/// Two implementations are foreseen and both are ordinary: the in-process loopback one this feature
/// ships, which owns a live match, and the remote one FR-4 adds, which owns a connection. A client
/// cannot tell them apart, and local play is therefore not a shortcut around the protocol - it runs
/// the same diff/apply pipeline the wire does (D-61).
/// </summary>
public interface IMatchGateway : IDisposable
{
    /// <summary>
    /// The match as it currently stands. Replaced wholesale as the match advances; never mutated in
    /// place, so a caller that captured it at the top of a frame keeps a consistent view for that
    /// whole frame.
    /// </summary>
    MatchSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Tells this gateway how much wall-clock time passed locally since the last call. This is a
    /// clock report, not a general-purpose "tick" method: the loopback implementation is the one
    /// that turns it into whole ticks and advances its own match, and the remote implementation
    /// FR-4 adds ignores it entirely, because there the server's scheduler owns the clock (D-62).
    /// </summary>
    /// <param name="elapsedMilliseconds">Elapsed milliseconds, never negative.</param>
    void Advance(long elapsedMilliseconds);

    /// <summary>
    /// Submits <paramref name="command"/> on behalf of this session's local player, and reports
    /// whether it was applied. The command names no player: attributing it is the gateway's job,
    /// never the client's (D-76).
    /// </summary>
    GatewayCommandResult Submit(GatewayCommand command);
}
