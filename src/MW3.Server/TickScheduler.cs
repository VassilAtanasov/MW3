using MW3.Core;

namespace MW3.Server;

/// <summary>
/// D-63: one hosted service, one 50 ms timer, walking every live session - no thread, task or timer
/// per match. This is the entire reason two sessions must share nothing: a session that touched
/// another's state would turn this single loop into a race.
/// </summary>
internal sealed class TickScheduler : BackgroundService
{
    private readonly MatchSessionRegistry _registry;
    private readonly ILogger<TickScheduler> _logger;

    // Public: the DI container's ActivatorUtilities only considers public constructors when
    // AddHostedService<T> resolves this type, even though the class itself is internal.
    public TickScheduler(MatchSessionRegistry registry, ILogger<TickScheduler> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Match.TickDurationMilliseconds));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var session in _registry.Snapshot())
            {
                try
                {
                    await session.TickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One session's failure must never take the scheduler - and every other live
                    // session - down with it.
                    _logger.LogError(ex, "Session {MatchId} failed to tick and was evicted.", session.MatchId);
                    _registry.Remove(session.MatchId);
                    continue;
                }

                if (session.ShouldEvict)
                {
                    _registry.Remove(session.MatchId);
                }
            }
        }
    }
}
