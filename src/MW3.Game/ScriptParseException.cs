namespace MW3.Game;

/// <summary>
/// Thrown when a `--script` file cannot be parsed. <see cref="LineNumber"/> is the 1-based line
/// that failed, so a caller can report it without re-deriving it from the message.
/// </summary>
public sealed class ScriptParseException : Exception
{
    public ScriptParseException(int lineNumber, string message)
        : base(message)
    {
        LineNumber = lineNumber;
    }

    public ScriptParseException()
    {
    }

    public ScriptParseException(string message)
        : base(message)
    {
    }

    public ScriptParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int LineNumber { get; }
}
