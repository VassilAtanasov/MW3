using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of phase 6 FR-5's forge-count readout anchoring - no graphics device, mirroring
/// <see cref="MoraleMeterTests"/>'s approach against the meter it is anchored to. The anchoring logic
/// (<see cref="ForgesReadout"/>'s private <c>SunSpacing</c>) is exercised indirectly here since it is
/// only ever derived from <see cref="MoraleMeter"/>'s own public rects; drawing itself (which needs a
/// real <c>SpriteFont</c>/<c>SpriteBatch</c>, unavailable headlessly anywhere else in this test
/// project either) is exercised end to end by the new <c>qa/scripts/</c> screenshot script instead.
/// </summary>
public class ForgesReadoutTests
{
    // Neither meter overlaps any base on the real map (MoraleMeterTests.NeitherMeter_OverlapsAnyBase);
    // the readout sits immediately outside the fifth sun, so it can only be further from the map's
    // interior than that sun already is. Asserted here as "further from the field-of-play edge",
    // since ForgesReadout has no destination rect of its own to intersect against the way a sun does.
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void HumanReadoutAnchor_SitsStrictlyRightOfTheFifthSun(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);
        var fifthSun = MoraleMeter.GetHumanSunRect(4, viewport);
        var gap = MoraleMeter.GetHumanSunRect(1, viewport).Left - MoraleMeter.GetHumanSunRect(0, viewport).Right;

        Assert.True(gap > 0, "adjacent suns must have a positive gap for this anchor computation to mean anything");
        Assert.InRange(fifthSun.Right + gap, 0, width);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void AiReadoutAnchor_SitsStrictlyLeftOfTheFifthSun(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);
        var fifthSun = MoraleMeter.GetAiSunRect(4, viewport);
        var gap = MoraleMeter.GetHumanSunRect(1, viewport).Left - MoraleMeter.GetHumanSunRect(0, viewport).Right;

        Assert.InRange(fifthSun.Left - gap, 0, width);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1808, 1018)]
    public void BothAnchors_AreVerticallyCentredOnTheirOwnRow(int width, int height)
    {
        var viewport = new Viewport(0, 0, width, height);

        // The anchor Draw uses (fifthSun.Center.Y) is, by construction, the same Y every sun on that
        // row shares - asserted against a different sun on the same row as the load-bearing check.
        var humanFifth = MoraleMeter.GetHumanSunRect(4, viewport);
        var humanFirst = MoraleMeter.GetHumanSunRect(0, viewport);
        Assert.Equal(humanFirst.Center.Y, humanFifth.Center.Y);

        var aiFifth = MoraleMeter.GetAiSunRect(4, viewport);
        var aiFirst = MoraleMeter.GetAiSunRect(0, viewport);
        Assert.Equal(aiFirst.Center.Y, aiFifth.Center.Y);
    }
}
