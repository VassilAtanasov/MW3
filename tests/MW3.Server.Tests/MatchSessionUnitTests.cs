using System.Net.WebSockets;
using MW3.Core;
using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// Drives <see cref="MatchSession"/> and <see cref="MatchSessionRegistry"/> directly (internal,
/// via <c>InternalsVisibleTo</c>) rather than through a socket, for the lifecycle rules a real
/// client can't easily force on demand: disconnect grace, AI substitution, and eviction.
/// </summary>
public sealed class MatchSessionUnitTests
{
    [Fact]
    public async Task ADisconnectedSession_SubstitutesAiAfterItsGracePeriod_AndTheMatchConcludes()
    {
        using var stub = new ClientWebSocket(); // never connected - stands in as "no live connection"
        // Time scale 1: ticking is a plain synchronous call here (no real 50 ms wait), so a large
        // iteration bound costs milliseconds, not minutes - and it keeps the grace period (D-65)
        // and the AI-substitute's own decision cadence at their real relative proportions, unlike a
        // high time scale where the whole match can conclude before grace elapses at all.
        using var session = new MatchSession("test-match", MapCatalog.Get(MapId.Small), timeScale: 1, stub);
        session.Disconnect();

        Assert.Equal(0, session.DisconnectedBeats);

        const int maxBeats = 200_000;
        for (var beat = 0; beat < maxBeats && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        // Either the human's own AI substitute helped decide the match (the grace period was
        // reached), or - looser but still meaningful - the match concluded on its own; either way
        // it must not still be running after 200,000 beats with nobody at the wheel.
        Assert.NotEqual(MatchOutcome.InProgress, session.Match.Outcome);
        Assert.True(session.DisconnectedBeats >= ServerTuning.DisconnectGraceTicks);
    }

    [Fact]
    public async Task ADisconnectedSession_AtATimeScaleCrossingManyDecisionBoundariesPerBeat_SubstitutesCorrectly()
    {
        // 200 ticks/beat crosses five of MatchRunner.DecisionIntervalTicks's 40-tick boundaries in a
        // single TickAsync call - exactly the shape a first cut of the substitute logic got wrong
        // (it advanced the whole beat first and only afterward replayed decisions per boundary,
        // so every replayed decision saw the beat's end state rather than its own boundary's). This
        // asserts the fixed, boundary-by-boundary interleaving reaches a sane, decided outcome
        // without throwing under that exact shape.
        using var stub = new ClientWebSocket();
        using var session = new MatchSession("test-match", MapCatalog.Get(MapId.Small), timeScale: 200, stub);
        session.Disconnect();

        for (var beat = 0; beat < 5000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        // At this time scale the opponent AI alone can decide the match against a wholly idle human
        // before the grace period (200 beats) even elapses - that is a legitimate outcome, not a
        // bug, so this does not assert the substitute activated (unlike the timeScale: 1 test above,
        // which is slow enough that it reliably does). What this test exists to prove is that
        // stepping boundary-by-boundary at a time scale five boundaries wide per beat runs to a
        // sane, decided conclusion without throwing.
        Assert.NotEqual(MatchOutcome.InProgress, session.Match.Outcome);
    }

    [Fact]
    public async Task ADecidedMatch_WithNoConnection_IsEvictable()
    {
        using var stub = new ClientWebSocket();
        using var session = new MatchSession("test-match", MapCatalog.Get(MapId.Small), timeScale: 5000, stub);
        session.Disconnect();

        for (var beat = 0; beat < 2000 && session.Match.Outcome == MatchOutcome.InProgress; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        Assert.NotEqual(MatchOutcome.InProgress, session.Match.Outcome);
        Assert.True(session.ShouldEvict);
    }

    [Fact]
    public async Task ADisconnectedSession_IsNeverEvictedImmediately_ButEventuallyIs()
    {
        using var stub = new ClientWebSocket();
        using var session = new MatchSession("test-match", MapCatalog.Get(MapId.Small), timeScale: 1, stub);
        session.Disconnect();

        Assert.False(session.ShouldEvict);

        // Whichever fires first - the match concludes while abandoned, or the idle timeout expires
        // with it still running - eviction must eventually be true. Which cause fires first is not
        // pinned down deterministically here (both are legitimate); ServerTuning.IdleEvictionTicks
        // is the outer bound either way.
        for (var beat = 0; beat < ServerTuning.IdleEvictionTicks && !session.ShouldEvict; beat++)
        {
            await session.TickAsync(CancellationToken.None);
        }

        Assert.True(session.ShouldEvict);
    }

    [Fact]
    public void TheRegistry_ReturnsToEmpty_AndHoldsNoReferenceToAnEvictedSession()
    {
        var registry = new MatchSessionRegistry();
        using var stub = new ClientWebSocket();
        using var session = new MatchSession("evict-me", MapCatalog.Get(MapId.Small), timeScale: 1, stub);

        Assert.True(registry.TryAdd(session));
        Assert.Equal(1, registry.Count);

        registry.Remove(session.MatchId);

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.Snapshot());
    }
}
