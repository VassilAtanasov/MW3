using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// Draws one player's <c>Forges: &lt;n&gt;</c> readout (phase 6 FR-5) - the one building value drawn
/// nowhere else on the map, since the buff it names lands on every combat everywhere rather than at
/// the forge itself. Mirrors <see cref="MoraleMeter"/>'s anchoring exactly: the human's readout sits
/// immediately right of the fifth (rightmost) bottom-left sun, vertically centred on that row; the
/// AI's sits immediately left of the fifth (leftmost) top-right sun, vertically centred on its own
/// row. Stateless and static, the same shape as <see cref="WaveColumnPresentation"/>'s helpers and
/// <see cref="MoraleMeter"/> itself: <see cref="MatchScreen"/> reads the live count fresh every
/// <see cref="MatchScreen.Draw"/> call and passes it in, so a capture, loss, or completed conversion
/// lands in the same frame it lands in <c>Match</c>. Always drawn, even at zero - a hidden-at-zero
/// readout would be indistinguishable from one not yet implemented.
/// </summary>
internal static class ForgesReadout
{
    /// <summary>
    /// The gap between the fifth sun and the readout - derived from two adjacent sun rects rather
    /// than a duplicated literal, so it always matches <see cref="MoraleMeter"/>'s own sun spacing
    /// exactly, however that spacing is tuned.
    /// </summary>
    private static int SunSpacing(Viewport viewport) =>
        MoraleMeter.GetHumanSunRect(1, viewport).Left - MoraleMeter.GetHumanSunRect(0, viewport).Right;

    /// <summary>
    /// Draws <paramref name="text"/> (already formatted by the caller - see
    /// <see cref="MatchScreen"/>'s change-only formatting, matching <see cref="MoraleMeter"/>'s own
    /// no-allocation-per-frame rule) beside the fifth sun of <paramref name="isHuman"/>'s meter, in
    /// <paramref name="ownerColor"/>.
    /// </summary>
    public static void Draw(SpriteBatch spriteBatch, SpriteFont font, Viewport viewport, string text, Color ownerColor, bool isHuman)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);

        var fifthSun = isHuman ? MoraleMeter.GetHumanSunRect(4, viewport) : MoraleMeter.GetAiSunRect(4, viewport);
        var gap = SunSpacing(viewport);
        var textSize = font.MeasureString(text);
        var verticalCenter = fifthSun.Center.Y;

        // The human row grows rightward, so the readout sits to the sun's right, left-anchored. The
        // AI row grows leftward, so it sits to the sun's left, right-anchored - text drawn from its
        // own width backward from the gap, so it never drifts as the string's length changes.
        var position = isHuman
            ? new Vector2(fifthSun.Right + gap, verticalCenter - (textSize.Y / 2f))
            : new Vector2(fifthSun.Left - gap - textSize.X, verticalCenter - (textSize.Y / 2f));

        spriteBatch.DrawString(font, text, position, ownerColor);
    }
}
