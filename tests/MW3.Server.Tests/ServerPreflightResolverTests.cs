using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MW3.Transport;

namespace MW3.Server.Tests;

/// <summary>
/// FR-5, D-81: the one probe-and-decide resolver both heads share. The success case needs a live
/// endpoint to probe, which is why these tests live here rather than in a new project - this project
/// already stands one up (<see cref="ServerFixture"/>).
/// </summary>
public sealed class ServerPreflightResolverTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _fixture;

    public ServerPreflightResolverTests(ServerFixture fixture) => _fixture = fixture;

    [Fact]
    public void ReachableServer_ReturnsAFactoryWithTheServersMapNames()
    {
        var result = ServerPreflightResolver.Resolve(_fixture.WebSocketUri.ToString(), timeScale: 1, TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureKind);
        Assert.Equal(new[] { "Small", "Medium", "Big" }, result.Factory!.MapNames);
    }

    [Fact]
    public void MalformedAddress_FailsAsMalformedWithoutAttemptingAConnection()
    {
        var result = ServerPreflightResolver.Resolve("not-a-url", timeScale: 1, TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(ServerPreflightFailureKind.Malformed, result.FailureKind);
        Assert.Contains("not-a-url", result.FailureDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusedConnection_FailsAsUnreachable()
    {
        // Nothing listens on this loopback port, so the OS refuses the connection immediately.
        var result = ServerPreflightResolver.Resolve("ws://127.0.0.1:1", timeScale: 1, TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(ServerPreflightFailureKind.Unreachable, result.FailureKind);
    }

    [Fact]
    public async Task ServerThatAcceptsButNeverAnswers_FailsAsUnreachableWithinTheTimeout_NotAHang()
    {
        // Accepts the TCP connection (so ConnectAsync itself does not fail fast) but never completes
        // the WebSocket handshake - the only way to prove the resolver is bounded by the timeout
        // rather than by whatever the OS or peer eventually does.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var stopwatch = Stopwatch.StartNew();
        var result = ServerPreflightResolver.Resolve(
            FormattableString.Invariant($"ws://127.0.0.1:{port}"), timeScale: 1, TimeSpan.FromMilliseconds(300));
        stopwatch.Stop();

        Assert.False(result.Succeeded);
        Assert.Equal(ServerPreflightFailureKind.Unreachable, result.FailureKind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Resolve took {stopwatch.Elapsed}, which is not bounded by its timeout.");

        using var accepted = await acceptTask;
    }

    [Fact]
    public void NonPositiveTimeScale_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ServerPreflightResolver.Resolve(_fixture.WebSocketUri.ToString(), timeScale: 0, TimeSpan.FromSeconds(5)));
    }
}
