using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game.Tests;

public class MoraleMeterTests
{
    private static Rectangle GetStrengthButtonRect(int index, Viewport viewport) =>
        (Rectangle)typeof(SendStrengthSelector)
            .GetMethod("GetButtonRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { index, viewport })!;

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void HumanSunRow_GrowsRightwardAndStaysWithinTheViewport(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        Rectangle? previous = null;
        for (var i = 0; i < 5; i++)
        {
            var rect = MoraleMeter.GetHumanSunRect(i, viewport);

            Assert.InRange(rect.Left, 0, width);
            Assert.InRange(rect.Right, 0, width);
            Assert.InRange(rect.Top, 0, height);
            Assert.InRange(rect.Bottom, 0, height);

            if (previous is Rectangle p)
            {
                Assert.True(rect.Left >= p.Right, $"sun {i} at {width}x{height} overlaps its predecessor");
            }

            previous = rect;
        }
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void AiSunRow_GrowsLeftwardFromTheRightEdgeAndStaysWithinTheViewport(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        Rectangle? previous = null;
        for (var i = 0; i < 5; i++)
        {
            var rect = MoraleMeter.GetAiSunRect(i, viewport);

            Assert.InRange(rect.Left, 0, width);
            Assert.InRange(rect.Right, 0, width);
            Assert.InRange(rect.Top, 0, height);
            Assert.InRange(rect.Bottom, 0, height);

            if (previous is Rectangle p)
            {
                Assert.True(rect.Right <= p.Left, $"sun {i} at {width}x{height} overlaps its predecessor");
            }

            previous = rect;
        }
    }

    // The human meter must never contest a press with SendStrengthSelector's bottom-left button
    // column - mirrors SendStrengthSelectorTests.EveryButton_IsFarEnoughFromEveryBase's approach.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void HumanMeter_NeverOverlapsTheStrengthSelector(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        for (var sun = 0; sun < 5; sun++)
        {
            var sunRect = MoraleMeter.GetHumanSunRect(sun, viewport);

            for (var button = 0; button < 4; button++)
            {
                var buttonRect = GetStrengthButtonRect(button, viewport);
                Assert.False(sunRect.Intersects(buttonRect), $"sun {sun} overlaps strength button {button} at {width}x{height}");
            }
        }
    }

    // Neither meter contests a press with any base on the real map (mirrors
    // SendStrengthSelectorTests.EveryButton_IsFarEnoughFromEveryBase) - corners stay clear because
    // MapLayout keeps every slot at least 0.12 units from any edge.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void NeitherMeter_OverlapsAnyBase(int width, int height)
    {
        var match = new Match();
        var viewport = new Viewport(0, 0, width, height);

        for (var i = 0; i < 5; i++)
        {
            var humanRect = MoraleMeter.GetHumanSunRect(i, viewport);
            var aiRect = MoraleMeter.GetAiSunRect(i, viewport);

            foreach (var b in match.Bases)
            {
                var basePixel = new Point((int)(b.Position.X * viewport.Width), (int)(b.Position.Y * viewport.Height));
                Assert.False(humanRect.Contains(basePixel), $"human sun {i} overlaps base {b.Id} at {width}x{height}");
                Assert.False(aiRect.Contains(basePixel), $"ai sun {i} overlaps base {b.Id} at {width}x{height}");
            }
        }
    }
}
