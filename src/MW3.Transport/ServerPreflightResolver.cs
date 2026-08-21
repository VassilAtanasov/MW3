using System.Net.WebSockets;

namespace MW3.Transport;

/// <summary>Why <see cref="ServerPreflightResolver.Resolve"/> could not produce a ready factory.</summary>
public enum ServerPreflightFailureKind
{
    /// <summary>The candidate address does not parse as an absolute ws/wss/http/https URL.</summary>
    Malformed,

    /// <summary>The address parsed, but the handshake did not complete within the given timeout.</summary>
    Unreachable,
}

/// <summary>
/// The outcome of one <see cref="ServerPreflightResolver.Resolve"/> call: either a ready remote
/// factory (the handshake succeeded and <see cref="IMatchGatewayFactory.MapNames"/> is populated), or
/// a typed reason a caller can act on without inspecting an exception.
/// </summary>
public sealed class ServerPreflightResult
{
    private ServerPreflightResult(IMatchGatewayFactory? factory, ServerPreflightFailureKind? failureKind, string? failureDetail)
    {
        Factory = factory;
        FailureKind = failureKind;
        FailureDetail = failureDetail;
    }

    /// <summary>True when <see cref="Factory"/> is a ready remote gateway factory.</summary>
    public bool Succeeded => Factory is not null;

    /// <summary>The ready factory, or null on failure.</summary>
    public IMatchGatewayFactory? Factory { get; }

    /// <summary>Why the probe failed, or null on success.</summary>
    public ServerPreflightFailureKind? FailureKind { get; }

    /// <summary>A human-readable detail naming the offending value and, on <see cref="ServerPreflightFailureKind.Unreachable"/>, the underlying error - never null on failure.</summary>
    public string? FailureDetail { get; }

    internal static ServerPreflightResult Success(IMatchGatewayFactory factory) => new(factory, null, null);

    internal static ServerPreflightResult Failure(ServerPreflightFailureKind kind, string detail) => new(null, kind, detail);
}

/// <summary>
/// One probe-and-decide resolver shared by every head (D-81): it takes a candidate server address and
/// a timeout, performs the <see cref="WireMessageKind.Hello"/>/<see cref="WireMessageKind.Welcome"/>
/// pre-flight handshake FR-4 already defines, and returns either a ready
/// <see cref="IMatchGatewayFactory"/> or a typed failure reason. It decides nothing about what a head
/// does with that reason - the desktop head exits 1 on either failure kind, the Android head falls
/// back to loopback on either - that policy stays in each head (D-81).
/// </summary>
public static class ServerPreflightResolver
{
    /// <summary>
    /// Resolves <paramref name="candidateAddress"/>. Pass <see cref="Timeout.InfiniteTimeSpan"/> for a
    /// caller that wants to wait for the OS-level connection outcome with no artificial bound (the
    /// desktop head's historical behaviour); pass a finite <paramref name="timeout"/> for a caller that
    /// must stay responsive regardless of what the address does (Android's D-82).
    /// </summary>
    public static ServerPreflightResult Resolve(string candidateAddress, long timeScale, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(candidateAddress);
        if (timeScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be positive.");
        }

        if (!Uri.TryCreate(candidateAddress, UriKind.Absolute, out var serverUri)
            || (serverUri.Scheme != "ws" && serverUri.Scheme != "wss" && serverUri.Scheme != "http" && serverUri.Scheme != "https"))
        {
            return ServerPreflightResult.Failure(
                ServerPreflightFailureKind.Malformed,
                FormattableString.Invariant($"'{candidateAddress}' is not a valid server URL."));
        }

        var webSocketUri = ToWebSocketUri(serverUri);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var factory = new RemoteMatchGatewayFactory(webSocketUri, timeScale, cts.Token);
            return ServerPreflightResult.Success(factory);
        }
        catch (OperationCanceledException)
        {
            return ServerPreflightResult.Failure(
                ServerPreflightFailureKind.Unreachable,
                FormattableString.Invariant($"'{candidateAddress}' did not respond within {timeout.TotalMilliseconds:0} ms."));
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidOperationException)
        {
            return ServerPreflightResult.Failure(
                ServerPreflightFailureKind.Unreachable,
                FormattableString.Invariant($"'{candidateAddress}' could not be reached: {ex.Message}"));
        }
    }

    // ws:// / wss:// for the actual WebSocket handshake, from whatever scheme the candidate address
    // was given in (http/https compose the same way a browser's ws upgrade does) - moved here
    // unchanged from MW3.Desktop's Program.cs, which now calls this resolver instead.
    private static Uri ToWebSocketUri(Uri serverUri)
    {
        var scheme = serverUri.Scheme switch
        {
            "https" or "wss" => "wss",
            _ => "ws",
        };

        return new UriBuilder(serverUri) { Scheme = scheme, Port = serverUri.Port }.Uri;
    }
}
