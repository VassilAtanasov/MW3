using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// Its own class, and so its own <see cref="ServerFixture"/> - the session cap test needs to start
/// from a registry it knows is empty, which sharing a fixture with any other test does not
/// guarantee (a session a prior test created is not evicted the instant its client disconnects).
/// </summary>
public sealed class SessionCapTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _fixture;

    public SessionCapTests(ServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ACreateSessionBeyondTheConcurrentSessionCap_IsRefusedWithAReason()
    {
        var clients = new List<TestWireClient>();
        try
        {
            for (var i = 0; i < ServerTuning.MaxConcurrentSessions; i++)
            {
                var client = await _fixture.ConnectAsync();
                clients.Add(client);
                await client.HandshakeAsync();
                var created = await client.CreateSessionAsync("Small", timeScale: 1);
                Assert.Equal(WireMessageKind.SessionCreated, created.Kind);
            }

            await using var oneTooMany = await _fixture.ConnectAsync();
            await oneTooMany.HandshakeAsync();
            var refused = await oneTooMany.CreateSessionAsync("Small", timeScale: 1);

            Assert.Equal(WireMessageKind.Error, refused.Kind);
            Assert.NotNull(refused.Reason);
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
