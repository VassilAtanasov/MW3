namespace MW3.Game;

/// <summary>A back request.</summary>
internal sealed record BackDirective(int Frame) : ScriptDirective(Frame);
