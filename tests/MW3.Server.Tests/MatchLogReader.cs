using System.Text;
using System.Text.Json;

namespace MW3.Server.Tests;

/// <summary>
/// A minimal, test-only JSON Lines reader for the FR-6 per-match log - not a shipped reader
/// (D-89: shipping one is the <b>Game logs, game replays</b> project's content). Reads line by
/// line so a file with a truncated final line (no trailing LF - a server killed mid-write) yields
/// every whole record before it and simply drops the unparseable tail, rather than throwing.
/// </summary>
internal static class MatchLogReader
{
    internal readonly record struct LogLine(string Kind, JsonElement Root);

    /// <summary>Every whole (fully written) record in <paramref name="path"/>, in file order.</summary>
    internal static IReadOnlyList<LogLine> ReadLines(string path)
    {
        var lines = new List<LogLine>();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            if (raw.Length == 0)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException)
            {
                // A truncated final line - not a whole record. Everything before it still stands.
                continue;
            }

            lines.Add(new LogLine(document.RootElement.GetProperty("kind").GetString()!, document.RootElement));
        }

        return lines;
    }

    /// <summary>True if <paramref name="lines"/> ends with a <c>trailer</c> record - a log with no trailer is a match whose server died mid-write.</summary>
    internal static bool IsComplete(IReadOnlyList<LogLine> lines) => lines.Count > 0 && lines[^1].Kind == "trailer";
}
