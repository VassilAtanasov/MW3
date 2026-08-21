using System.Globalization;
using MW3.Core;
using MW3.Game;
using MW3.Transport;

var smokeTest = args.Contains("--smoke");

long timeScale = 1;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--time-scale" && i + 1 < args.Length)
    {
        var raw = args[i + 1];
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeScale) || timeScale <= 0)
        {
            Console.Error.WriteLine($"--time-scale value '{raw}' must be a positive integer.");
            Environment.Exit(1);
        }
    }
}

string? serverUrl = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--server" && i + 1 < args.Length)
    {
        serverUrl = args[i + 1];
    }
}

// Phase 8 FR-3, D-74: this head is the composition root. It is the only thing in the desktop build
// that can see MW3.Core (and, from FR-4, MW3.Transport), so it is where the gateway factory is
// constructed and from where it is injected into the client - which is typed against MW3.Protocol
// alone. FR-4, D-79: --time-scale reaches the server through the factory rather than being applied
// client-side, because under --server the server's scheduler owns the clock (D-62).
IMatchGatewayFactory gatewayFactory;
if (serverUrl is null)
{
    gatewayFactory = new LoopbackMatchGatewayFactory();
}
else
{
    // FR-5, D-81: the pre-flight handshake now runs through the one resolver MW3.Transport shares
    // with the Android head, rather than a second copy of it here. --server is an explicit,
    // unattended-QA flag, so this head's policy on either failure kind is unchanged from before the
    // move: write the offending value to stderr and exit 1 before any graphics device is created,
    // exactly as --map and --time-scale already validate before one exists. Infinite timeout
    // preserves this head's historical behaviour of waiting for the OS-level connection outcome
    // rather than an artificial bound - only Android (D-82) needs one.
    var result = ServerPreflightResolver.Resolve(serverUrl, timeScale, Timeout.InfiniteTimeSpan);
    if (!result.Succeeded)
    {
        Console.Error.WriteLine($"--server value '{serverUrl}' {(result.FailureKind == ServerPreflightFailureKind.Malformed ? "is not a valid server URL" : "could not be reached")}: {result.FailureDetail}");
        Environment.Exit(1);
        return;
    }

    gatewayFactory = result.Factory!;
}

string? screenshotPath = null;
string? scriptPath = null;
string? dumpStatePath = null;
string? bootMap = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--screenshot" && i + 1 < args.Length)
    {
        screenshotPath = args[i + 1];
    }
    else if (args[i] == "--script" && i + 1 < args.Length)
    {
        scriptPath = args[i + 1];
    }
    else if (args[i] == "--dump-state" && i + 1 < args.Length)
    {
        dumpStatePath = args[i + 1];
    }
    else if (args[i] == "--map")
    {
        // Validated against the factory's own list rather than against a map enum this head no
        // longer needs to know (D-74) - and still before any graphics device exists, so a bad value
        // exits 1 without ever opening a window.
        if (i + 1 >= args.Length || !TryResolveMapName(gatewayFactory, args[i + 1], out var parsedMap))
        {
            var offending = i + 1 < args.Length ? args[i + 1] : "<missing>";
            Console.Error.WriteLine($"--map value '{offending}' must be one of: small, medium, big.");
            Environment.Exit(1);
        }
        else
        {
            bootMap = parsedMap;
        }
    }
}

static bool TryResolveMapName(IMatchGatewayFactory factory, string raw, out string? mapName)
{
    foreach (var name in factory.MapNames)
    {
        if (string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
        {
            mapName = name;
            return true;
        }
    }

    mapName = null;
    return false;
}

IReadOnlyList<ScriptDirective>? scriptDirectives = null;
if (scriptPath is not null)
{
    try
    {
        scriptDirectives = ScriptParser.Parse(scriptPath);
    }
    catch (ScriptParseException ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.Exit(1);
    }
}

using var game = new MW3Game(gatewayFactory, exitAfterFirstDraw: smokeTest, screenshotPath: screenshotPath, dumpStatePath: dumpStatePath, scriptDirectives: scriptDirectives, timeScale: timeScale, bootMap: bootMap);
game.Run();
