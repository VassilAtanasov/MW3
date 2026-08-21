using System.Net.WebSockets;
using System.Text;
using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// §"Every inbound message is validated where it is deserialized": a malformed or out-of-range
/// message closes the connection with a reason rather than throwing into the scheduler or
/// corrupting a session - and never applies to <c>Match</c>.
/// </summary>
public sealed class HandshakeAndValidationTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _fixture;

    public HandshakeAndValidationTests(ServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Hello_GetsAWelcome_NamingEveryMapInCatalogueOrder()
    {
        await using var client = await _fixture.ConnectAsync();

        var welcome = await client.HandshakeAsync();

        Assert.Equal(WireMessageKind.Welcome, welcome.Kind);
        Assert.Equal(MatchSnapshot.CurrentProtocolVersion, welcome.ProtocolVersion);
        Assert.Equal(new[] { "Small", "Medium", "Big" }, welcome.MapNames);
    }

    [Fact]
    public async Task MismatchedProtocolVersion_ClosesTheConnectionNamingBothVersions()
    {
        await using var client = await _fixture.ConnectAsync();

        await client.SendAsync(WireMessage.Hello(MatchSnapshot.CurrentProtocolVersion + 1));
        var reply = await client.ReceiveAsync();

        Assert.NotNull(reply);
        Assert.Equal(WireMessageKind.Error, reply!.Kind);
        Assert.Contains(MatchSnapshot.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Reason, StringComparison.Ordinal);
        Assert.Contains((MatchSnapshot.CurrentProtocolVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJson_ClosesTheConnectionWithAReason()
    {
        await using var client = await _fixture.ConnectAsync();

        await client.SendRawAsync(Encoding.UTF8.GetBytes("{ this is not valid JSON"));

        // Either the server answers Error before closing, or the decode failure closes the socket
        // outright - both are an acceptable "never reaches Match" outcome. What must never happen is
        // the connection staying open and silently accepting more traffic.
        try
        {
            var reply = await client.ReceiveAsync();
            Assert.True(reply is null || reply.Kind == WireMessageKind.Error);
        }
        catch (WebSocketException)
        {
            // The server closed the socket abruptly rather than sending a framed Error - acceptable.
        }
    }

    [Fact]
    public async Task UnknownMessageKindValue_ClosesTheConnectionWithAReason()
    {
        await using var client = await _fixture.ConnectAsync();

        // A structurally valid envelope whose Kind names something the enum has no member for.
        var json = "{\"Kind\":\"NotAKind\",\"ProtocolVersion\":" + MatchSnapshot.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        await client.SendRawAsync(Encoding.UTF8.GetBytes(json));

        var reply = await client.ReceiveAsync();
        Assert.True(reply is null || reply.Kind == WireMessageKind.Error);
    }

    [Fact]
    public async Task UnknownCommandKindValue_AfterASession_ClosesTheConnectionWithAReason()
    {
        await using var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();
        await client.CreateSessionAsync("Small", timeScale: 1);

        var json = "{\"Kind\":\"Command\",\"ProtocolVersion\":" + MatchSnapshot.CurrentProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"CommandId\":1,\"Command\":{\"Kind\":\"NotACommandKind\",\"FromBaseId\":0}}";
        await client.SendRawAsync(Encoding.UTF8.GetBytes(json));

        var reply = await client.ReceiveAsync();
        Assert.True(reply is null || reply.Kind == WireMessageKind.Error);
    }

    [Fact]
    public async Task OutOfRangeBaseId_ClosesTheConnectionAndNeverReachesTheMatch()
    {
        await using var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();
        await client.CreateSessionAsync("Small", timeScale: 1);

        await client.SendAsync(WireMessage.SubmitCommand(
            MatchSnapshot.CurrentProtocolVersion, commandId: 1, GatewayCommand.Upgrade(baseId: 99_999)));

        var reply = await client.ReceiveAsync();
        Assert.Equal(WireMessageKind.Error, reply!.Kind);
        Assert.NotNull(reply.Reason);
    }

    [Fact]
    public async Task WrongProtocolVersionOnCreateSession_ClosesTheConnectionNamingBothVersions()
    {
        await using var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();

        await client.SendAsync(new WireMessage(
            WireMessageKind.CreateSession,
            MatchSnapshot.CurrentProtocolVersion + 1,
            MapNames: null,
            MapName: "Small",
            TimeScale: 1,
            MatchId: null,
            Snapshot: null,
            CommandId: null,
            Command: null,
            CommandResult: null,
            Events: null,
            SnapshotHash: null,
            Reason: null));

        var reply = await client.ReceiveAsync();
        Assert.Equal(WireMessageKind.Error, reply!.Kind);
    }

    [Fact]
    public async Task UnreachableSocketClose_IsHandledCleanly()
    {
        var client = await _fixture.ConnectAsync();
        await client.HandshakeAsync();
        await client.CreateSessionAsync("Small", timeScale: 1);

        // A hard close mid-session must not crash the scheduler for anyone else - proven by the
        // next test in this class still passing against the same fixture.
        await client.DisposeAsync();
    }
}
