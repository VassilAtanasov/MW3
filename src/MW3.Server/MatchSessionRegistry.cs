using System.Collections.Concurrent;

namespace MW3.Server;

/// <summary>
/// Every live session, keyed by match id (§"MW3.Server": <c>MatchSessionRegistry</c>). A plain
/// concurrent dictionary rather than anything fancier - one writer (the connection handler adding a
/// new session, or removing an evicted one) and one reader (<see cref="TickScheduler"/> walking it
/// every beat), and neither needs more than what <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// already gives for free.
/// </summary>
internal sealed class MatchSessionRegistry
{
    private readonly ConcurrentDictionary<string, MatchSession> _sessions = new();

    internal int Count => _sessions.Count;

    /// <summary>False if a session with this id already exists - callers assign ids from <see cref="Guid.NewGuid"/>, so a collision would be a real bug.</summary>
    internal bool TryAdd(MatchSession session) => _sessions.TryAdd(session.MatchId, session);

    internal void Remove(string matchId)
    {
        if (_sessions.TryRemove(matchId, out var session))
        {
            session.Dispose();
        }
    }

    /// <summary>A snapshot of every live session, safe to iterate while the registry mutates concurrently.</summary>
    internal IReadOnlyCollection<MatchSession> Snapshot() => _sessions.Values.ToArray();
}
