using Xunit;

// Several test classes each start a real Kestrel host with WebSocket connections and a live 50 ms
// scheduler; running them concurrently contends for the thread pool and makes the scheduler's real
// timer lag unpredictably, which is exactly what a match-completion test with a wall-clock timeout
// cannot tolerate. Sequential execution keeps every fixture's timer honest.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
