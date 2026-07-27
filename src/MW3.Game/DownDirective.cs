namespace MW3.Game;

/// <summary>A pointer press at a normalized 0..1 position.</summary>
internal sealed record DownDirective(int Frame, double X, double Y) : ScriptDirective(Frame);
