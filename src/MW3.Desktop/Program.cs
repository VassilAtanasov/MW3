using System.Globalization;
using MW3.Game;

var smokeTest = args.Contains("--smoke");

string? screenshotPath = null;
string? scriptPath = null;
string? dumpStatePath = null;
long timeScale = 1;
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

using var game = new MW3Game(exitAfterFirstDraw: smokeTest, screenshotPath: screenshotPath, dumpStatePath: dumpStatePath, scriptDirectives: scriptDirectives, timeScale: timeScale);
game.Run();
