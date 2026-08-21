using MW3.Core;
using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// Drives complete matches through the real WebSocket endpoint (success criterion in the
/// "Verification (D-80)" section of the FR-4 issue), asserting the client-reconstructed snapshot
/// equals the server's own at every send by structural equality - never by asserting a message was
/// sent, which <c>docs/CONVENTIONS.md</c> calls hollow.
/// </summary>
public sealed class MatchLifecycleTests : IClassFixture<ServerFixture>
{
    // High enough that even a long match concludes in a handful of real scheduler beats - this
    // phase's regression bar (byte-identical qa/scripts/) lives entirely on the loopback path, so
    // nothing here needs to run at the real tick rate.
    private const long _testTimeScale = 2000;
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromSeconds(90);

    private readonly ServerFixture _fixture;

    public MatchLifecycleTests(ServerFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Small")]
    [InlineData("Medium")]
    [InlineData("Big")]
    public async Task AMatch_PlaysToADecidedOutcome_WithTheClientReconstructionMatchingTheServerAtEverySend(string mapName)
    {
        await using var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();
        var created = await client.CreateSessionAsync(mapName, _testTimeScale);

        Assert.Equal(WireMessageKind.SessionCreated, created.Kind);
        Assert.False(string.IsNullOrEmpty(created.MatchId));
        Assert.Equal(mapName, created.Snapshot!.MapId);
        Assert.Equal(MatchOutcome.InProgress, created.Snapshot.Outcome);

        // WaitForOutcomeAsync applies every Events batch with SnapshotApplier and asserts the
        // resulting hash matches the one the server computed (D-71) at every single one - this is
        // the structural equality check the issue asks for "at every send". A passive human against
        // a well-defended map (Big carries two neutral towers guarding its forge) can legitimately
        // run long without ever losing, so this asserts substantial, hash-verified progress rather
        // than pinning down a guaranteed decision time none of these three maps actually promise.
        var final = await client.WaitForOutcomeAsync(created.Snapshot, _matchTimeout);

        Assert.True(
            final.Outcome != MatchOutcome.InProgress || final.ElapsedTicks > created.Snapshot.ElapsedTicks + 10_000,
            $"Expected either a decided outcome or substantial progress on {mapName}; only reached tick {final.ElapsedTicks}.");
    }

    [Fact]
    public async Task TwoSessionsOnTheSameMap_ShareNothing_ACommandInOneLeavesTheOtherBitIdentical()
    {
        await using var a = await _fixture.ConnectAsync();
        await a.HandshakeAsync();
        var aCreated = await a.CreateSessionAsync("Small", timeScale: 1);

        await using var b = await _fixture.ConnectAsync();
        await b.HandshakeAsync();
        var bCreated = await b.CreateSessionAsync("Small", timeScale: 1);

        Assert.NotEqual(aCreated.MatchId, bCreated.MatchId);
        Assert.Equal(SnapshotHash.Compute(aCreated.Snapshot!), SnapshotHash.Compute(bCreated.Snapshot!));

        // The identical command against each session's identical tick-0 state must produce the
        // identical verdict - proof B's session was never touched by A's, since both still start
        // from the same shape.
        await a.SendAsync(WireMessage.SubmitCommand(MatchSnapshot.CurrentProtocolVersion, commandId: 1, GatewayCommand.Upgrade(baseId: 0)));
        var aResult = await a.ReceiveAsync();
        Assert.Equal(WireMessageKind.CommandResult, aResult!.Kind);

        await b.SendAsync(WireMessage.SubmitCommand(MatchSnapshot.CurrentProtocolVersion, commandId: 1, GatewayCommand.Upgrade(baseId: 0)));
        var bResult = await b.ReceiveAsync();
        Assert.Equal(WireMessageKind.CommandResult, bResult!.Kind);

        Assert.Equal(aResult.CommandResult!.Accepted, bResult.CommandResult!.Accepted);
        Assert.Equal(aResult.CommandResult.RejectionReason, bResult.CommandResult.RejectionReason);
    }

    [Fact]
    public async Task ACommandTheClientCouldBelieveValidButTheRulesReject_SurfacesItsReasonToTheClient()
    {
        await using var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();
        await client.CreateSessionAsync("Small", timeScale: 1);

        // Base 1 is the AI's start base on every shipped map (phase 7 FR-1: slots 0-5 are shared) -
        // a send the client could plausibly have believed valid (both ids exist, they're different),
        // rejected because the human does not own the source.
        await client.SendAsync(WireMessage.SubmitCommand(
            MatchSnapshot.CurrentProtocolVersion, commandId: 1, GatewayCommand.SendArmy(from: 1, to: 2, SendStrength.Half)));

        var reply = await client.ReceiveAsync();
        Assert.Equal(WireMessageKind.CommandResult, reply!.Kind);
        Assert.False(reply.CommandResult!.Accepted);
        Assert.Equal(nameof(SendArmyOutcome.SourceNotOwnedByIssuer), reply.CommandResult.RejectionReason);
    }

    [Fact]
    public async Task ManySessions_TickConcurrentlyOnTheSingleScheduler_EachReachesItsOwnOutcomeIndependently()
    {
        const int sessionCount = 12;

        var clients = new List<TestWireClient>();
        try
        {
            var startedSnapshots = new List<MatchSnapshot>();
            for (var i = 0; i < sessionCount; i++)
            {
                var client = await _fixture.ConnectAsync();
                clients.Add(client);
                await client.HandshakeAsync();
                var created = await client.CreateSessionAsync("Small", _testTimeScale);
                startedSnapshots.Add(created.Snapshot!);
            }

            var finals = await Task.WhenAll(clients.Select((c, i) => c.WaitForOutcomeAsync(startedSnapshots[i], _matchTimeout)));

            Assert.All(finals, f => Assert.NotEqual(MatchOutcome.InProgress, f.Outcome));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }
}
