using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MW3.Server;

var builder = WebApplication.CreateBuilder(args);

// Localhost only, no TLS, no recurring cost (§6) - the default ASP.NET Core urls include an https
// endpoint that needs a dev certificate, which this phase has no use for.
builder.WebHost.UseUrls("http://localhost:5180");

// FR-6, D-86: --log-dir <path>, defaulting to logs/ under the content root. Resolved to an absolute
// path and validated writable before app.StartAsync() - the port-clash check above already
// establishes "fail at startup, not at first use", and validating after binding would leave a
// listening server that cannot log.
var logDirectory = ResolveLogDirectory(args, builder.Environment.ContentRootPath);
if (!TryEnsureWritable(logDirectory, out var logDirectoryError))
{
    await Console.Error.WriteLineAsync($"MW3.Server could not use log directory '{logDirectory}': {logDirectoryError}");
    Environment.Exit(1);
    return;
}

builder.Services.AddSingleton<MatchSessionRegistry>();
builder.Services.AddSingleton(new MatchLogDirectory(logDirectory));
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
    var logDir = context.RequestServices.GetRequiredService<MatchLogDirectory>();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await ConnectionHandler.HandleAsync(socket, registry, logDir.Path, context.RequestAborted);
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

await Console.Out.WriteLineAsync($"MW3.Server logging matches to {logDirectory}");

await app.WaitForShutdownAsync();

// Resolves --log-dir <path> to an absolute path, defaulting to logs/ under the content root.
static string ResolveLogDirectory(string[] args, string contentRoot)
{
    var flagIndex = Array.IndexOf(args, "--log-dir");
    var raw = flagIndex >= 0 && flagIndex + 1 < args.Length ? args[flagIndex + 1] : "logs";
    return Path.GetFullPath(raw, contentRoot);
}

// Creates path if absent and proves it is writable with a throwaway probe file.
static bool TryEnsureWritable(string path, out string error)
{
    try
    {
        Directory.CreateDirectory(path);
        var probePath = Path.Combine(path, $".write-check-{Guid.NewGuid():n}");
        File.WriteAllBytes(probePath, Array.Empty<byte>());
        File.Delete(probePath);
        error = "";
        return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
    {
        error = ex.Message;
        return false;
    }
}

// Exposes the top-level program to WebApplicationFactory<Program> in MW3.Server.Tests, which spins
// this host up in-memory to drive the real WebSocket endpoint end to end - the standard ASP.NET
// Core pattern for testing a minimal API without binding a real port.
public partial class Program;
