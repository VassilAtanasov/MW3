using Microsoft.Xna.Framework;

namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of FR-2's illustrated building sprites - no graphics device, mirroring how
/// <see cref="WaveColumnPresentationTests"/> exercises layout math. The acceptance bar is
/// silhouette and growth, not pixel-art taste: a village reads as a dwelling that gains caps
/// with the existing snapshot level, a tower is masonry-with-cannon, a forge is the most
/// complex of the three, and the garrison cloud is a white bubble that can carry 0.
/// </summary>
public class BuildingSpriteRasterizerTests
{
    private const int Size = BuildingSpriteRasterizer.Size;

    [Fact]
    public void Village_Level1_HasCapWiderThanStem_AndFeetOnBothSides()
    {
        var (body, _) = Rasterize(BaseType.Producer, level: 1);

        var capWidth = MaxOpaqueWidth(body, yStart: 0.28f, yEnd: 0.50f);
        var stemWidth = MaxOpaqueWidth(body, yStart: 0.60f, yEnd: 0.76f);
        Assert.True(capWidth > stemWidth * 1.35f, $"cap width {capWidth} should outspan stem {stemWidth}");

        Assert.True(HasOpaqueIn(body, 0.28f, 0.42f, 0.82f, 0.94f), "left foot");
        Assert.True(HasOpaqueIn(body, 0.58f, 0.72f, 0.82f, 0.94f), "right foot");
    }

    [Fact]
    public void Village_Level1_HasWindowsOnTheStem()
    {
        var (_, detail) = Rasterize(BaseType.Producer, level: 1);

        Assert.True(
            HasDarkDetailIn(detail, 0.38f, 0.62f, 0.54f, 0.70f),
            "stem should carry window panes, not a blank shaft");
    }

    [Fact]
    public void Village_HigherLevel_GrowsExtraCaps_NotOnlyAScale()
    {
        var (level1, _) = Rasterize(BaseType.Producer, level: 1);
        var (level5, _) = Rasterize(BaseType.Producer, level: 5);

        var level1LeftCaps = OpaqueCount(level1, 0.00f, 0.22f, 0.12f, 0.48f);
        var level5LeftCaps = OpaqueCount(level5, 0.00f, 0.22f, 0.12f, 0.48f);
        var level1RightCaps = OpaqueCount(level1, 0.80f, 1.00f, 0.12f, 0.48f);
        var level5RightCaps = OpaqueCount(level5, 0.80f, 1.00f, 0.12f, 0.48f);

        Assert.True(level5LeftCaps > level1LeftCaps * 2, "level 5 adds a left-hand cap the level-1 house does not have");
        Assert.True(level5RightCaps > level1RightCaps * 2, "level 5 adds a right-hand cap the level-1 house does not have");

        var scaledLevel1 = ScaleOccupancy(level1, 1.45f);
        var agreement = OccupancyAgreement(scaledLevel1, Occupancy(level5));
        Assert.True(
            agreement < 0.72f,
            $"a uniformly scaled level-1 house still agreed with level 5 at {agreement:0.00}; extra caps must change the silhouette");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    public void Village_EachLevel_AddsOpaquePixelsOverThePrevious(int lower, int higher)
    {
        var (low, _) = Rasterize(BaseType.Producer, lower);
        var (high, _) = Rasterize(BaseType.Producer, higher);

        Assert.True(
            OpaqueCount(high, 0f, 1f, 0f, 1f) > OpaqueCount(low, 0f, 1f, 0f, 1f),
            $"level {higher} should be a larger cluster than level {lower}");
    }

    [Fact]
    public void Village_UsesTheExistingLevel_AndClampsRatherThanInventingAWireField()
    {
        var (level1, _) = Rasterize(BaseType.Producer, level: 1);
        var (clampedLow, _) = Rasterize(BaseType.Producer, level: 0);
        var (level5, _) = Rasterize(BaseType.Producer, level: 5);
        var (clampedHigh, _) = Rasterize(BaseType.Producer, level: 99);

        Assert.True(OccupancyAgreement(Occupancy(level1), Occupancy(clampedLow)) > 0.98f);
        Assert.True(OccupancyAgreement(Occupancy(level5), Occupancy(clampedHigh)) > 0.98f);
    }

    [Fact]
    public void Tower_HasMasonryCrenellationsAndACannon()
    {
        var (body, detail) = Rasterize(BaseType.Tower, level: 1);

        Assert.True(HasOpaqueIn(body, 0.86f, 0.98f, 0.12f, 0.26f), "cannon barrel reaches the right-hand edge");
        Assert.True(
            CountHorizontalRuns(body, y: 0.22f) >= 4,
            "the top should read as crenellations, not a single roof");
        Assert.True(
            HasDarkDetailIn(detail, 0.32f, 0.68f, 0.34f, 0.72f),
            "keep faces should carry masonry / window detail");
    }

    [Fact]
    public void Forge_HasDomeChimneyAndMoreDetailThanVillageOrTower()
    {
        var (forgeBody, forgeDetail) = Rasterize(BaseType.Forge, level: 1);
        var (villageL1, _) = Rasterize(BaseType.Producer, level: 1);
        var (towerBody, towerDetail) = Rasterize(BaseType.Tower, level: 1);

        Assert.True(HasOpaqueIn(forgeBody, 0.28f, 0.56f, 0.18f, 0.40f), "crystal dome");
        Assert.True(HasOpaqueIn(forgeBody, 0.70f, 0.88f, 0.12f, 0.30f), "chimney");

        // Type-to-type: a forge is a single workshop more articulated than a single dwelling or
        // a tower. A level-5 village is a cluster of dwellings - more caps, not a more complex type.
        var forgeEdges = EdgeCount(forgeBody);
        Assert.True(forgeEdges > EdgeCount(villageL1), "forge should out-articulate a single village");
        Assert.True(forgeEdges > EdgeCount(towerBody), "forge should out-articulate a tower");
        Assert.True(OpaqueCount(forgeDetail, 0f, 1f, 0f, 1f) > OpaqueCount(towerDetail, 0f, 1f, 0f, 1f));

        // Distinct silhouettes: none of the three families is a recolour of another.
        Assert.True(OccupancyAgreement(Occupancy(forgeBody), Occupancy(villageL1)) < 0.55f);
        Assert.True(OccupancyAgreement(Occupancy(forgeBody), Occupancy(towerBody)) < 0.55f);
        Assert.True(OccupancyAgreement(Occupancy(villageL1), Occupancy(towerBody)) < 0.55f);
    }

    [Fact]
    public void BodyLayer_IsWhiteSoOwnerIsAPaletteTint()
    {
        foreach (var type in new[] { BaseType.Producer, BaseType.Tower, BaseType.Forge })
        {
            var (body, _) = Rasterize(type, level: 3);
            for (var i = 0; i < body.Length; i++)
            {
                var pixel = body[i];
                if (pixel.A == 0)
                {
                    continue;
                }

                Assert.True(
                    pixel.R == 255 && pixel.G == 255 && pixel.B == 255,
                    $"{type} body pixel {i} was {pixel} - owner tint requires a white body");
            }
        }
    }

    [Fact]
    public void GarrisonCloud_IsAWhiteBubbleWithADownwardTail()
    {
        var cloud = new Color[Size * Size];
        BuildingSpriteRasterizer.RasterizeGarrisonCloud(cloud);

        Assert.True(HasOpaqueIn(cloud, 0.35f, 0.65f, 0.30f, 0.55f), "cloud body");
        Assert.True(HasOpaqueIn(cloud, 0.45f, 0.55f, 0.78f, 0.92f), "downward tail toward the sprite");

        for (var i = 0; i < cloud.Length; i++)
        {
            var pixel = cloud[i];
            if (pixel.A == 0)
            {
                continue;
            }

            Assert.True(pixel.R == 255 && pixel.G == 255 && pixel.B == 255, "cloud must stay white");
        }
    }

    [Fact]
    public void Rasterize_RejectsABufferOfTheWrongLength()
    {
        var body = new Color[Size * Size];
        var wrong = new Color[4];

        Assert.Throws<ArgumentException>(() => BuildingSpriteRasterizer.Rasterize(BaseType.Producer, 1, body, wrong));
        Assert.Throws<ArgumentNullException>(() => BuildingSpriteRasterizer.Rasterize(BaseType.Producer, 1, body, null!));
    }

    private static (Color[] Body, Color[] Detail) Rasterize(BaseType type, int level)
    {
        var body = new Color[Size * Size];
        var detail = new Color[Size * Size];
        BuildingSpriteRasterizer.Rasterize(type, level, body, detail);
        return (body, detail);
    }

    private static bool IsOpaque(Color pixel) => pixel.A >= 128;

    private static bool HasOpaqueIn(Color[] pixels, float x0, float x1, float y0, float y1)
    {
        return OpaqueCount(pixels, x0, x1, y0, y1) > 8;
    }

    private static bool HasDarkDetailIn(Color[] detail, float x0, float x1, float y0, float y1)
    {
        var minX = (int)(x0 * Size);
        var maxX = (int)(x1 * Size);
        var minY = (int)(y0 * Size);
        var maxY = (int)(y1 * Size);
        var dark = 0;
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var pixel = detail[(y * Size) + x];
                if (pixel.A >= 128 && pixel.R < 80 && pixel.G < 80 && pixel.B < 90)
                {
                    dark++;
                }
            }
        }

        return dark > 8;
    }

    private static int EdgeCount(Color[] pixels)
    {
        var edges = 0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!IsOpaque(pixels[(y * Size) + x]))
                {
                    continue;
                }

                var up = y == 0 || !IsOpaque(pixels[((y - 1) * Size) + x]);
                var down = y == Size - 1 || !IsOpaque(pixels[((y + 1) * Size) + x]);
                var left = x == 0 || !IsOpaque(pixels[(y * Size) + x - 1]);
                var right = x == Size - 1 || !IsOpaque(pixels[(y * Size) + x + 1]);
                if (up || down || left || right)
                {
                    edges++;
                }
            }
        }

        return edges;
    }

    private static int OpaqueCount(Color[] pixels, float x0, float x1, float y0, float y1)
    {
        var minX = Math.Clamp((int)(x0 * Size), 0, Size - 1);
        var maxX = Math.Clamp((int)(x1 * Size), 0, Size);
        var minY = Math.Clamp((int)(y0 * Size), 0, Size - 1);
        var maxY = Math.Clamp((int)(y1 * Size), 0, Size);
        var count = 0;
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                if (IsOpaque(pixels[(y * Size) + x]))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int MaxOpaqueWidth(Color[] pixels, float yStart, float yEnd)
    {
        var minY = (int)(yStart * Size);
        var maxY = (int)(yEnd * Size);
        var maxWidth = 0;
        for (var y = minY; y < maxY; y++)
        {
            var width = 0;
            for (var x = 0; x < Size; x++)
            {
                if (IsOpaque(pixels[(y * Size) + x]))
                {
                    width++;
                }
            }

            if (width > maxWidth)
            {
                maxWidth = width;
            }
        }

        return maxWidth;
    }

    private static int CountHorizontalRuns(Color[] pixels, float y)
    {
        var row = (int)(y * Size);
        var runs = 0;
        var inside = false;
        for (var x = 0; x < Size; x++)
        {
            var opaque = IsOpaque(pixels[(row * Size) + x]);
            if (opaque && !inside)
            {
                runs++;
            }

            inside = opaque;
        }

        return runs;
    }

    private static bool[] Occupancy(Color[] pixels)
    {
        var mask = new bool[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            mask[i] = IsOpaque(pixels[i]);
        }

        return mask;
    }

    private static bool[] ScaleOccupancy(Color[] pixels, float scale)
    {
        var mask = new bool[Size * Size];
        var center = (Size - 1) / 2f;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var srcX = (int)MathF.Round(center + ((x - center) / scale));
                var srcY = (int)MathF.Round(center + ((y - center) / scale));
                if (srcX < 0 || srcX >= Size || srcY < 0 || srcY >= Size)
                {
                    continue;
                }

                mask[(y * Size) + x] = IsOpaque(pixels[(srcY * Size) + srcX]);
            }
        }

        return mask;
    }

    private static float OccupancyAgreement(bool[] a, bool[] b)
    {
        var both = 0;
        var either = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] || b[i])
            {
                either++;
            }

            if (a[i] && b[i])
            {
                both++;
            }
        }

        return either == 0 ? 1f : both / (float)either;
    }
}
