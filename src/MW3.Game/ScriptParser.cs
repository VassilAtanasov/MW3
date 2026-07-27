using System.Globalization;

namespace MW3.Game;

/// <summary>
/// Parses a `--script` file into ordered directives. One `&lt;frame&gt; &lt;directive&gt; [args]`
/// per line; blank lines are skipped and `#` starts a full-line comment. Directives are
/// `down &lt;x&gt; &lt;y&gt;`, `up &lt;x&gt; &lt;y&gt;` (normalized 0..1 pointer coordinates),
/// `back`, and `wait` (a no-argument timeline marker that only extends playback).
/// </summary>
public static class ScriptParser
{
    public static IReadOnlyList<ScriptDirective> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directives = new List<ScriptDirective>();
        var lines = File.ReadAllLines(path);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            directives.Add(ParseLine(line, lineNumber: i + 1));
        }

        return directives;
    }

    private static ScriptDirective ParseLine(string line, int lineNumber)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2 || !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
        {
            throw new ScriptParseException(lineNumber, $"line {lineNumber}: expected '<frame> <directive> [args]', found '{line}'.");
        }

        return tokens[1] switch
        {
            "down" when tokens.Length == 4 && TryParseCoordinates(tokens, out var downX, out var downY) =>
                new DownDirective(frame, downX, downY),
            "up" when tokens.Length == 4 && TryParseCoordinates(tokens, out var upX, out var upY) =>
                new UpDirective(frame, upX, upY),
            "back" when tokens.Length == 2 =>
                new BackDirective(frame),
            "wait" when tokens.Length == 2 =>
                new WaitDirective(frame),
            _ => throw new ScriptParseException(lineNumber, $"line {lineNumber}: unrecognized directive '{line}'."),
        };
    }

    private static bool TryParseCoordinates(string[] tokens, out double x, out double y)
    {
        var xParsed = double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        var yParsed = double.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        return xParsed && yParsed;
    }
}
