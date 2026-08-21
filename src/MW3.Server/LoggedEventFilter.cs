namespace MW3.Server;

/// <summary>
/// Which <see cref="MatchEvent"/> kinds the per-match log records (FR-6, D-88). Commands are the
/// log's authoritative replay input; events are a curated, explicitly-derived narrative -
/// <see cref="MatchEventKind.BaseChanged"/>, <see cref="MatchEventKind.ArmyChanged"/> and
/// <see cref="MatchEventKind.AvailableActionsChanged"/> are never logged, and
/// <see cref="MatchEventKind.MoraleChanged"/> only when the player's <c>MoraleLevel</c> actually
/// changed, not merely their points.
/// </summary>
internal static class LoggedEventFilter
{
    /// <summary>
    /// True if <paramref name="matchEvent"/> should be written to the log. <paramref name="before"/>
    /// is the snapshot the event's batch was diffed from - the only kind that needs it is
    /// <see cref="MatchEventKind.MoraleChanged"/>, to compare the player's level before and after.
    /// </summary>
    internal static bool ShouldLog(MatchEvent matchEvent, MatchSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(matchEvent);
        ArgumentNullException.ThrowIfNull(before);

        switch (matchEvent.Kind)
        {
            case MatchEventKind.BaseCaptured:
            case MatchEventKind.ArmyLaunched:
            case MatchEventKind.ArmyRemoved:
            case MatchEventKind.ConstructionStarted:
            case MatchEventKind.ConstructionCompleted:
            case MatchEventKind.ForgeCountChanged:
            case MatchEventKind.MatchEnded:
                return true;

            case MatchEventKind.MoraleChanged:
                var previous = FindPlayer(before, matchEvent.PlayerId!.Value);
                return previous is null || previous.MoraleLevel != matchEvent.Player!.MoraleLevel;

            case MatchEventKind.BaseChanged:
            case MatchEventKind.ArmyChanged:
            case MatchEventKind.AvailableActionsChanged:
            default:
                return false;
        }
    }

    private static PlayerSnapshot? FindPlayer(MatchSnapshot snapshot, int playerId)
    {
        var players = snapshot.Players;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].Id == playerId)
            {
                return players[i];
            }
        }

        return null;
    }
}
