using Microsoft.Xna.Framework;

namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of FR-3's marching-unit sprite - no graphics device. The bar is silhouette, not
/// pixel-art taste: a round head forward of a compact body, with feet on the baseline.
/// </summary>
public class UnitSpriteRasterizerTests
{
    private const int _size = UnitSpriteRasterizer.Size;

    [Fact]
    public void Rasterize_HasRoundHeadForwardOfBody()
    {
        var (body, _) = Rasterize();

        var headWidth = MaxOpaqueWidth(body, 0.44f, 0.80f, 0.14f, 0.46f);
        var torsoWidth = MaxOpaqueWidth(body, 0.30f, 0.74f, 0.44f, 0.76f);
        Assert.True(headWidth > 0f, "head should be visible");
        Assert.True(torsoWidth > 0f, "torso should be visible");
        Assert.True(headWidth >= torsoWidth * 0.7f, "head should read as wide as the shoulders");
    }

    [Fact]
    public void Rasterize_HasFacialDetailOnTheForwardFace()
    {
        var (_, detail) = Rasterize();

        Assert.True(
            HasDarkDetailIn(detail, 0.64f, 0.76f, 0.24f, 0.36f),
            "an eye on the forward face should distinguish a character from a pip");
    }

    [Fact]
    public void Rasterize_HasFeetOnTheBaseline()
    {
        var (body, _) = Rasterize();

        Assert.True(HasOpaqueIn(body, 0.30f, 0.48f, 0.74f, 0.92f), "left foot");
        Assert.True(HasOpaqueIn(body, 0.50f, 0.68f, 0.74f, 0.92f), "right foot");
    }

    private static (Color[] Body, Color[] Detail) Rasterize()
    {
        var body = new Color[_size * _size];
        var detail = new Color[_size * _size];
        UnitSpriteRasterizer.Rasterize(body, detail);
        return (body, detail);
    }

    private static bool HasOpaqueIn(Color[] pixels, float xStart, float xEnd, float yStart, float yEnd)
    {
        var minX = (int)(xStart * _size);
        var maxX = (int)(xEnd * _size);
        var minY = (int)(yStart * _size);
        var maxY = (int)(yEnd * _size);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (pixels[(y * _size) + x].A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasDarkDetailIn(Color[] pixels, float xStart, float xEnd, float yStart, float yEnd)
    {
        var minX = (int)(xStart * _size);
        var maxX = (int)(xEnd * _size);
        var minY = (int)(yStart * _size);
        var maxY = (int)(yEnd * _size);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var c = pixels[(y * _size) + x];
                if (c.A > 0 && c.R < 200 && c.G < 200 && c.B < 200)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static float MaxOpaqueWidth(Color[] pixels, float xStart, float xEnd, float yStart, float yEnd)
    {
        var minX = (int)(xStart * _size);
        var maxX = (int)(xEnd * _size);
        var minY = (int)(yStart * _size);
        var maxY = (int)(yEnd * _size);
        var maxWidth = 0;

        for (var y = minY; y <= maxY; y++)
        {
            var left = -1;
            var right = -1;
            for (var x = minX; x <= maxX; x++)
            {
                if (pixels[(y * _size) + x].A > 0)
                {
                    if (left < 0)
                    {
                        left = x;
                    }

                    right = x;
                }
            }

            if (left >= 0)
            {
                maxWidth = Math.Max(maxWidth, right - left + 1);
            }
        }

        return maxWidth;
    }
}
