using System.Globalization;
using MW3.Core;
using MW3.Game;

var smokeTest = args.Contains("--smoke");

string? screenshotPath = null;
string? scriptPath = null;
string? dumpStatePath = null;
long timeScale = 1;
MapId? bootMap = null;
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
    else if (args[i] == "--time-scale" && i + 1 < args.Length)
    {
        var raw = args[i + 1];
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeScale) || timeScale <= 0)
        {
            Console.Error.WriteLine($"--time-scale value '{raw}' must be a positive integer.");
            Environment.Exit(1);
        }
    }
    else if (args[i] == "--map")
    {
        if (i + 1 >= args.Length || !TryParseMapId(args[i + 1], out var parsedMap))
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

static bool TryParseMapId(string raw, out MapId mapId)
{
    switch (raw.ToLowerInvariant())
    {
        case "small":
            mapId = MapId.Small;
            return true;
        case "medium":
            mapId = MapId.Medium;
            return true;
        case "big":
            mapId = MapId.Big;
            return true;
        default:
            mapId = default;
            return false;
    }
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

using var game = new MW3Game(exitAfterFirstDraw: smokeTest, screenshotPath: screenshotPath, dumpStatePath: dumpStatePath, scriptDirectives: scriptDirectives, timeScale: timeScale, bootMap: bootMap);
game.Run();
