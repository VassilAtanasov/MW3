namespace MW3.Core;

/// <summary>
/// A position on the map normalized to the 0..1 range on both axes, resolved to pixels only in
/// presentation (D-14). Never an engine vector type (D-2).
/// </summary>
public readonly record struct MapPoint(double X, double Y);
