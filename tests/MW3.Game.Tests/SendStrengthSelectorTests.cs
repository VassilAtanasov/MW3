using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game.Tests;

public class SendStrengthSelectorTests
{
    private static Rectangle GetButtonRect(int index, Viewport viewport) =>
        (Rectangle)typeof(SendStrengthSelector)
            .GetMethod("GetButtonRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { index, viewport })!;

    private static MapPoint CenterOf(Rectangle rect, Viewport viewport) =>
        new((rect.X + (rect.Width / 2.0)) / viewport.Width, (rect.Y + (rect.Height / 2.0)) / viewport.Height);

    [Fact]
    public void DefaultsToHalf()
    {
        var selector = new SendStrengthSelector();

        Assert.Equal(SendStrength.Half, selector.SelectedStrength);
    }

    [Theory]
    [InlineData(0, SendStrength.Quarter)]
    [InlineData(1, SendStrength.Half)]
    [InlineData(2, SendStrength.ThreeQuarters)]
    [InlineData(3, SendStrength.Full)]
    public void Activate_SetsTheMatchingStrength(int buttonIndex, SendStrength expected)
    {
        var selector = new SendStrengthSelector();

        selector.Activate(buttonIndex);

        Assert.Equal(expected, selector.SelectedStrength);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void HitTestButton_ResolvesEachButtonAtItsOwnCenter(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        for (var i = 0; i < 4; i++)
        {
            var center = CenterOf(GetButtonRect(i, viewport), viewport);
            Assert.Equal(i, SendStrengthSelector.HitTestButton(center, viewport));
        }
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void HitTestButton_ResolvesToNoneInTheGapBetweenTwoButtons(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        var lower = GetButtonRect(0, viewport);
        var upper = GetButtonRect(1, viewport);
        var gapY = (lower.Top + upper.Bottom) / 2.0;
        var gapPoint = new MapPoint((lower.X + (lower.Width / 2.0)) / viewport.Width, gapY / viewport.Height);

        Assert.Null(SendStrengthSelector.HitTestButton(gapPoint, viewport));
    }

    // The control must never contest a press with a base on the real map (FR-2's own acceptance
    // criterion) - checked at both target resolutions, against every real base position, not a
    // synthetic one (mirrors BaseActionMenuTests.TwoButtons_NeverOverlap's approach).
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void EveryButton_IsFarEnoughFromEveryBase(int width, int height)
    {
        var match = new Match();
        var viewport = new Viewport(0, 0, width, height);

        for (var i = 0; i < 4; i++)
        {
            var center = CenterOf(GetButtonRect(i, viewport), viewport);

            foreach (var b in match.Bases)
            {
                var dx = center.X - b.Position.X;
                var dy = center.Y - b.Position.Y;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                Assert.True(
                    distance >= HitTester.SelectionThresholdUnits,
                    $"button {i} at {width}x{height} is only {distance:F3} from base {b.Id} at {b.Position}");
            }
        }
    }
}
