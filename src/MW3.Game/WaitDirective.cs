namespace MW3.Game;

/// <summary>A timeline marker with no effect, letting a script extend to a chosen frame.</summary>
internal sealed record WaitDirective(int Frame) : ScriptDirective(Frame);
