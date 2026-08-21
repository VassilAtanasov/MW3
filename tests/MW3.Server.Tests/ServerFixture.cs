using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MW3.Server.Tests;

/// <summary>
/// Runs a real <c>MW3.Server</c> host - real Kestrel, real WebSocket handshake over loopback TCP,
/// bound to an OS-assigned port so tests never collide - wired up exactly like
/// <c>src/MW3.Server/Program.cs</c>, minus the console banner and the exit-on-port-clash path
/// (there is no clash to check: nothing else asked for this port).
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private WebApplication? _app;

    internal Uri WebSocketUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<MatchSessionRegistry>();
        builder.Services.AddHostedService<TickScheduler>();

        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var registry = context.RequestServices.GetRequiredService<MatchSessionRegistry>();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await ConnectionHandler.HandleAsync(socket, registry, context.RequestAborted);
        });

        await app.StartAsync();
        _app = app;

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        WebSocketUri = new UriBuilder(new Uri(address)) { Scheme = "ws" }.Uri;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Opens a fresh WebSocket connection to this server - one per match (§"The connection is per match").</summary>
    internal async Task<TestWireClient> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri, cancellationToken).ConfigureAwait(false);
        return new TestWireClient(socket);
    }
}
