using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MW3.Server;

var builder = WebApplication.CreateBuilder(args);

// Localhost only, no TLS, no recurring cost (§6) - the default ASP.NET Core urls include an https
// endpoint that needs a dev certificate, which this phase has no use for.
builder.WebHost.UseUrls("http://localhost:5180");

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

try
{
    await app.StartAsync();
}
catch (IOException ex)
{
    // Covers Microsoft.AspNetCore.Connections.AddressInUseException, thrown when the port is
    // already bound - exits non-zero rather than silently binding elsewhere.
    await Console.Error.WriteLineAsync($"MW3.Server could not start: {ex.Message}");
    Environment.Exit(1);
}

var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
foreach (var address in addresses)
{
    await Console.Out.WriteLineAsync($"MW3.Server listening on {address}");
}

await app.WaitForShutdownAsync();

// Exposes the top-level program to WebApplicationFactory<Program> in MW3.Server.Tests, which spins
// this host up in-memory to drive the real WebSocket endpoint end to end - the standard ASP.NET
// Core pattern for testing a minimal API without binding a real port.
public partial class Program;
