namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of phase 6 FR-5's pure triangle geometry - no graphics device, mirroring how
/// <see cref="WaveColumnPresentationTests"/> exercises <see cref="WaveColumnPresentation"/>.
/// </summary>
public class TriangleGeometryTests
{
    [Fact]
    public void Contains_TheApexPixel_IsInside()
    {
        // The apex row (y = 0) is a single pixel wide, not a mathematical point, once rasterized -
        // exactly its centre column, since Contains samples each row's far (bottom) edge rather than
        // its centre specifically so the apex row isn't left empty (see Contains's own doc comment).
        Assert.True(TriangleGeometry.Contains(pixelX: 64, pixelY: 0, diameter: 128));
    }

    [Fact]
    public void Contains_TheApexRowsFarCorners_AreOutside()
    {
        Assert.False(TriangleGeometry.Contains(pixelX: 0, pixelY: 0, diameter: 128));
        Assert.False(TriangleGeometry.Contains(pixelX: 127, pixelY: 0, diameter: 128));
    }

    [Fact]
    public void Contains_TheBaseRowsFarCorners_AreInside()
    {
        // The base row (y = diameter - 1) spans the full width - the triangle's widest row.
        Assert.True(TriangleGeometry.Contains(pixelX: 0, pixelY: 127, diameter: 128));
        Assert.True(TriangleGeometry.Contains(pixelX: 127, pixelY: 127, diameter: 128));
    }

    [Fact]
    public void Contains_TheCentrePixel_IsInside()
    {
        Assert.True(TriangleGeometry.Contains(pixelX: 64, pixelY: 64, diameter: 128));
    }

    [Theory]
    [InlineData(64)] // apex row
    [InlineData(32)]
    [InlineData(96)]
    [InlineData(127)] // base row
    public void Contains_IsSymmetricAboutTheVerticalCentreLine(int y)
    {
        for (var dx = 1; dx < 64; dx++)
        {
            Assert.Equal(TriangleGeometry.Contains(64 - dx, y, 128), TriangleGeometry.Contains(64 + dx - 1, y, 128));
        }
    }

    /// <summary>Each row's width never shrinks going down from the apex to the base.</summary>
    [Fact]
    public void Contains_WidthIsNonDecreasing_FromApexToBase()
    {
        var previousWidth = -1;
        for (var y = 0; y < 128; y++)
        {
            var width = 0;
            for (var x = 0; x < 128; x++)
            {
                if (TriangleGeometry.Contains(x, y, 128))
                {
                    width++;
                }
            }

            Assert.True(width >= previousWidth, $"row {y}'s width {width} is narrower than row {y - 1}'s {previousWidth}");
            previousWidth = width;
        }
    }

    [Fact]
    public void IncenterYFraction_IsBetweenTheBoundingBoxCentreAndTheBase()
    {
        // The incenter is pulled toward the wider base, away from the apex - strictly past the
        // bounding box's own geometric centre (0.5) but never past the base itself (1.0).
        var fraction = TriangleGeometry.IncenterYFraction();

        Assert.InRange(fraction, 0.5f, 1.0f);
    }

    [Fact]
    public void InradiusFraction_IsSmallerThanTheCircumscribedCirclesRadius()
    {
        // The circle glyph's own radius is 0.5 of the diameter (it touches all four sides of the
        // same bounding box); the triangle's incircle must be strictly smaller, since a triangle
        // inscribes less area than a circle at the same bounding box - the whole reason this class
        // exists rather than reusing the circle's text-sizing formula.
        var fraction = TriangleGeometry.InradiusFraction();

        Assert.True(fraction > 0f);
        Assert.True(fraction < 0.5f);
    }

    /// <summary>
    /// The incircle - centred at the incenter, radius <see cref="TriangleGeometry.InradiusFraction"/>
    /// - fits entirely inside the rasterized triangle: every pixel on its boundary is itself inside.
    /// This is the property <see cref="MatchScreen"/> relies on to keep the garrison digits off the
    /// sloped sides.
    /// </summary>
    [Fact]
    public void TheIncircle_FitsEntirelyInsideTheRasterizedTriangle()
    {
        const int diameter = 200; // large enough that rounding at the boundary doesn't dominate
        var centerX = diameter / 2f;
        var centerY = TriangleGeometry.IncenterYFraction() * diameter;
        var inradius = TriangleGeometry.InradiusFraction() * diameter;

        for (var angleDegrees = 0; angleDegrees < 360; angleDegrees += 3)
        {
            var radians = angleDegrees * MathF.PI / 180f;

            // Pulled slightly inside the boundary (0.98x) so this checks the incircle itself rather
            // than being defeated by ordinary pixel-quantization at its exact tangent edge.
            var x = centerX + (MathF.Cos(radians) * inradius * 0.98f);
            var y = centerY + (MathF.Sin(radians) * inradius * 0.98f);

            Assert.True(
                TriangleGeometry.Contains((int)MathF.Floor(x), (int)MathF.Floor(y), diameter),
                $"incircle point at {angleDegrees} degrees ({x}, {y}) falls outside the triangle");
        }
    }
}
