using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace MW3.Transport;

/// <summary>
/// An <see cref="IMatchGateway"/> that reaches a match over a WebSocket to <c>MW3.Server</c>
/// (D-77). It holds a connection, not a <c>Match</c> - it never runs the rules, only
/// reconstructs a snapshot from the events the server sends (D-61's pipeline, now over a real wire).
/// One instance per match: the connection opens at construction and closes at <see cref="Dispose"/>,
/// matching the gateway lifecycle exactly (§ "The connection is per match").
/// </summary>
public sealed class RemoteMatchGateway : IMatchGateway
{
    // Generous for a localhost round trip (D-78 says ~0.1-1 ms against a 16 ms frame); this is an
    // engineering constant bounding a hang, not a tuning value the game's economy depends on, so it
    // does not route through the REQUIREMENTS.md tuning table (D-22 is about numbers that reach a
    // gameplay call site).
    private const int _submitTimeoutMilliseconds = 2000;

    private readonly ClientWebSocket _socket;
    private readonly IWireCodec _codec;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<GatewayCommandResult>> _pendingCommands = new();
    private readonly Task _receiveLoopTask;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private int _nextCommandId;
    private volatile MatchSnapshot _currentSnapshot;
    private volatile bool _disposed;

    /// <summary>Connects to <paramref name="serverUri"/> and starts a match on <paramref name="mapName"/>.</summary>
    public RemoteMatchGateway(Uri serverUri, string mapName, long timeScale, IWireCodec codec)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        ArgumentNullException.ThrowIfNull(mapName);
        ArgumentNullException.ThrowIfNull(codec);

        _codec = codec;
        _socket = new ClientWebSocket();

        try
        {
            _socket.ConnectAsync(serverUri, CancellationToken.None).GetAwaiter().GetResult();

            SendAsync(WireMessage.Hello(MatchSnapshot.CurrentProtocolVersion)).GetAwaiter().GetResult();
            var welcome = ReceiveHandshakeMessage(WireMessageKind.Welcome);
            RequireMatchingProtocolVersion(welcome);

            SendAsync(WireMessage.CreateSession(MatchSnapshot.CurrentProtocolVersion, mapName, timeScale)).GetAwaiter().GetResult();
            var sessionCreated = ReceiveHandshakeMessage(WireMessageKind.SessionCreated);
            RequireMatchingProtocolVersion(sessionCreated);

            MatchId = sessionCreated.MatchId!;
            _currentSnapshot = sessionCreated.Snapshot!;
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        _receiveLoopTask = Task.Run(() => RunReceiveLoopAsync(_receiveLoopCts.Token));
    }

    /// <summary>The id the server assigned this match (§ wire protocol: "matchId is assigned by the server").</summary>
    public string MatchId { get; }

    /// <inheritdoc />
    public MatchSnapshot CurrentSnapshot => _currentSnapshot;

    /// <summary>
    /// A no-op, as <see cref="IMatchGateway.Advance"/>'s own doc comment says a remote implementation
    /// may be: the server's scheduler owns the clock (D-62), not the client.
    /// </summary>
    public void Advance(long elapsedMilliseconds)
    {
        // Intentionally empty.
    }

    /// <inheritdoc />
    public GatewayCommandResult Submit(GatewayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandId = Interlocked.Increment(ref _nextCommandId);
        var completion = new TaskCompletionSource<GatewayCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[commandId] = completion;

        try
        {
            SendAsync(WireMessage.SubmitCommand(MatchSnapshot.CurrentProtocolVersion, commandId, command)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            _pendingCommands.TryRemove(commandId, out _);
            return GatewayCommandResult.Rejected(FormattableString.Invariant($"The connection to the server failed: {ex.Message}"));
        }

        var winner = Task.WhenAny(completion.Task, Task.Delay(_submitTimeoutMilliseconds)).GetAwaiter().GetResult();
        if (!ReferenceEquals(winner, completion.Task))
        {
            _pendingCommands.TryRemove(commandId, out _);
            return GatewayCommandResult.Rejected(
                FormattableString.Invariant($"The server did not answer command {commandId} within {_submitTimeoutMilliseconds} ms."));
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _receiveLoopCts.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disposed", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
        }
        catch (WebSocketException)
        {
            // The far side may already be gone - closing is best-effort.
        }

        try
        {
            _receiveLoopTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation is how the loop is told to stop.
        }

        foreach (var pending in _pendingCommands.Values)
        {
            pending.TrySetResult(GatewayCommandResult.Rejected("The gateway was disposed before the server answered."));
        }

        _receiveLoopCts.Dispose();
        _socket.Dispose();
        _sendGate.Dispose();
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? bytes;
            try
            {
                bytes = await WebSocketFraming.ReceiveAsync(_socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }

            if (bytes is null)
            {
                return;
            }

            WireMessage message;
            try
            {
                message = _codec.Decode(bytes);
            }
            catch (Exception)
            {
                await Console.Error.WriteLineAsync("RemoteMatchGateway: received a malformed message from the server; closing.").ConfigureAwait(false);
                return;
            }

            switch (message.Kind)
            {
                case WireMessageKind.Events:
                    if (!ApplyEvents(message))
                    {
                        return;
                    }

                    break;

                case WireMessageKind.CommandResult:
                    if (_pendingCommands.TryRemove(message.CommandId!.Value, out var completion))
                    {
                        completion.TrySetResult(message.CommandResult!);
                    }

                    break;

                case WireMessageKind.Error:
                    await Console.Error.WriteLineAsync(FormattableString.Invariant($"RemoteMatchGateway: server error: {message.Reason}")).ConfigureAwait(false);
                    return;

                default:
                    await Console.Error.WriteLineAsync(FormattableString.Invariant($"RemoteMatchGateway: unexpected message kind '{message.Kind}' after the handshake.")).ConfigureAwait(false);
                    return;
            }
        }
    }

    /// <summary>
    /// Applies one <see cref="WireMessageKind.Events"/> message to the current snapshot and checks
    /// the result against the hash the server computed (D-71). A mismatch is a desync: closes the
    /// connection and names both hashes, on the grounds that a client that keeps drawing a diverged
    /// board is worse than one that stops. Returns false when the receive loop should stop.
    /// </summary>
    private bool ApplyEvents(WireMessage message)
    {
        var previous = _currentSnapshot;
        MatchSnapshot applied;
        try
        {
            applied = SnapshotApplier.Apply(message.Events!, previous);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(FormattableString.Invariant($"RemoteMatchGateway: could not apply the server's event batch: {ex.Message}"));
            return false;
        }

        var computedHash = SnapshotHash.Compute(applied);
        if (computedHash != message.SnapshotHash!.Value)
        {
            Console.Error.WriteLine(FormattableString.Invariant(
                $"RemoteMatchGateway: snapshot desync at tick {applied.ElapsedTicks} - server hash {message.SnapshotHash.Value:x16}, client hash {computedHash:x16}."));
            return false;
        }

        // A single reference write - CurrentSnapshot is volatile, so a caller on another thread
        // never observes a torn read, and never needs a lock to avoid one.
        _currentSnapshot = applied;
        return true;
    }

    private WireMessage ReceiveHandshakeMessage(WireMessageKind expectedKind)
    {
        var bytes = WebSocketFraming.ReceiveAsync(_socket, CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException(FormattableString.Invariant($"The server closed the connection instead of sending {expectedKind}."));

        var message = _codec.Decode(bytes);
        if (message.Kind == WireMessageKind.Error)
        {
            throw new InvalidOperationException(FormattableString.Invariant($"The server refused the connection: {message.Reason}"));
        }

        if (message.Kind != expectedKind)
        {
            throw new InvalidOperationException(FormattableString.Invariant($"Expected {expectedKind} from the server but got {message.Kind}."));
        }

        return message;
    }

    private static void RequireMatchingProtocolVersion(WireMessage message)
    {
        if (message.ProtocolVersion != MatchSnapshot.CurrentProtocolVersion)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Protocol version mismatch: client is {MatchSnapshot.CurrentProtocolVersion}, server is {message.ProtocolVersion}."));
        }
    }

    private async Task SendAsync(WireMessage message)
    {
        var bytes = _codec.Encode(message);
        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await WebSocketFraming.SendAsync(_socket, bytes, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
