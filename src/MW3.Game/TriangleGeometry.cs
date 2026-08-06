namespace MW3.Game;

/// <summary>
/// Pure geometry for the forge's upward-pointing triangle glyph (phase 6 FR-5) - decides nothing and
/// draws nothing, mirroring how <see cref="WaveColumnPresentation"/> keeps FR-4's layout math
/// headlessly testable. The triangle this class describes has its apex at the top-centre of a
/// <c>diameter</c> x <c>diameter</c> square and its base along the square's bottom edge - the same
/// square <see cref="MatchScreen"/> already uses for the circle and square glyphs, so a forge takes
/// up the same footprint on the map as any other base.
/// <para>
/// A triangle inscribes less area than a circle at the same bounding box, so centring the garrison
/// text on the box's geometric centre (what the circle and square glyphs do) would let the digits
/// overhang the sloped sides. <see cref="IncenterYFraction"/> and <see cref="InradiusFraction"/> give
/// the triangle's own centre - the incenter, the point equidistant from all three sides - and the
/// radius of the largest circle that fits inside it at that point, so the caller can size and
/// position text the same way <c>MatchScreen</c> already sizes it for the circle, just against the
/// triangle's true inscribed circle rather than its bounding box.
/// </para>
/// </summary>
internal static class TriangleGeometry
{
    /// <summary>
    /// The incenter's vertical position, as a fraction of <c>diameter</c> down from the apex (0 at
    /// the apex, 1 at the base). Derived once, not looked up from a stored constant: for the apex-up
    /// isoceles triangle this class describes - apex <c>(r, 0)</c>, base corners <c>(0, D)</c> and
    /// <c>(D, D)</c> where <c>D</c> is the diameter and <c>r = D/2</c> - the incenter's x-coordinate
    /// is <c>r</c> by left-right symmetry, and its y-coordinate is the length-weighted average of the
    /// three vertices' y-coordinates: <c>(a*0 + b*D + c*D) / (a+b+c)</c>, where <c>a</c> is the base
    /// length <c>D</c> and <c>b = c</c> are the two equal legs' length <c>sqrt(r² + D²)</c>. Dividing
    /// through by <c>D</c> gives a dimensionless fraction independent of the actual pixel diameter.
    /// </summary>
    public static float IncenterYFraction()
    {
        const float r = 0.5f; // D = 1, so the apex sits at (0.5, 0) and radius is 0.5.
        var leg = MathF.Sqrt((r * r) + (1f * 1f));
        return (2f * leg) / (1f + (2f * leg));
    }

    /// <summary>
    /// The incircle's radius - the largest circle centred on the incenter that still fits entirely
    /// inside the triangle - as a fraction of <c>diameter</c>. Derived from the standard identity
    /// <c>inradius = Area / semiperimeter</c>: for this triangle, <c>Area = D²/2</c> (base <c>D</c>,
    /// height <c>D</c>) and the semiperimeter is <c>(D + 2*sqrt(r² + D²)) / 2</c>.
    /// </summary>
    public static float InradiusFraction()
    {
        const float r = 0.5f;
        var leg = MathF.Sqrt((r * r) + (1f * 1f));
        var area = 0.5f; // D = 1: base 1, height 1, area = base*height/2.
        var semiperimeter = (1f + (2f * leg)) / 2f;
        return area / semiperimeter;
    }

    /// <summary>
    /// Whether the pixel at <paramref name="pixelX"/>, <paramref name="pixelY"/> falls inside the
    /// triangle inscribed in a <paramref name="diameter"/> x <paramref name="diameter"/> square: apex
    /// at the top-centre, base along the bottom edge. The triangle's half-width grows linearly from 0
    /// at the apex to <c>diameter / 2</c> at the base, sampled at each row's <b>far</b> (bottom) edge
    /// rather than its centre - the circle and square rasterizers sample at the pixel centre, but a
    /// centre sample here would leave the apex row entirely empty (its centre lies at a half-width of
    /// under half a pixel, narrower than any pixel can render), producing a flat-topped shape instead
    /// of a point. Sampling the far edge is the standard conservative rule for rasterizing a shape
    /// that must come to a point - it fills the apex row with exactly its centre column and widens
    /// from there, and still fills the base row completely (the far edge of the last row is the
    /// bounding box's own bottom edge).
    /// </summary>
    public static bool Contains(int pixelX, int pixelY, int diameter)
    {
        var x = pixelX + 0.5f;
        var radius = diameter / 2f;

        var t = (pixelY + 1) / (float)diameter; // the row's far edge: 1/diameter at the apex row, 1 at the base row.
        var halfWidthAtY = radius * t;

        return x >= radius - halfWidthAtY && x <= radius + halfWidthAtY;
    }
}
