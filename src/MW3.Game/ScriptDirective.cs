namespace MW3.Game;

/// <summary>
/// One parsed line of a `--script` file. Concrete cases are internal - callers outside
/// `MW3.Game` only ever hold the list <see cref="ScriptParser.Parse"/> returns, never construct or
/// pattern-match on a specific directive.
/// </summary>
public abstract record ScriptDirective(int Frame);
