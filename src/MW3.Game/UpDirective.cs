namespace MW3.Game;

/// <summary>A pointer release at a normalized 0..1 position.</summary>
internal sealed record UpDirective(int Frame, double X, double Y) : ScriptDirective(Frame);
