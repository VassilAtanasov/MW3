using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Draws one player's morale as a fixed-position row of 5 sun indicators, filled left-to-right up
/// to <see cref="MoraleState.Level"/> (0-5) - whole-level display only, no partial-progress fill
/// (FR-5, REQUIREMENTS.md §6 leaves that optional and this feature does not claim it). Stateless
/// and static, the same shape as <see cref="WaveColumnPresentation"/>'s helpers: <see cref="Match"/>
/// is read fresh every <see cref="MatchScreen.Draw"/> call, never cached at screen entry, so a
/// morale change lands in the same frame it takes effect.
///
/// The human meter anchors bottom-left and the AI meter top-right (matching the owner colors
/// <see cref="MatchScreen"/> already uses), but neither literally touches its corner: both rows
/// are inset far enough to clear <see cref="SendStrengthSelector"/>'s bottom-left button column and
/// every base slot on every <see cref="MapCatalog"/> map, none of which sit closer than 0.12 to any
/// edge.
/// </summary>
internal static class MoraleMeter
{
    private const int _sunCount = 5;
    private const float _sunSizeFraction = 0.028f;
    private const float _sunSpacingFraction = 0.01f;

    // Clears SendStrengthSelector's single bottom-left button column (margin + buttonSize, D-2's
    // own fractions: 0.02 + 0.08 of the min dimension) with room to spare, and sits above the
    // bottom edge by the same margin the selector itself uses.
    private const float _marginFraction = 0.02f;
    private const float _humanRowLeftFraction = 0.14f;

    private static readonly Color _unfilledColor = Color.DimGray;

    /// <summary>
    /// Sun <paramref name="index"/>'s destination rectangle for the human (bottom-left) meter, whose
    /// row grows rightward starting clear of the strength selector's button column.
    /// </summary>
    public static Rectangle GetHumanSunRect(int index, Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var sunSize = (int)(minDimension * _sunSizeFraction);
        var spacing = (int)(minDimension * _sunSpacingFraction);
        var margin = (int)(minDimension * _marginFraction);

        var left = (int)(minDimension * _humanRowLeftFraction) + (index * (sunSize + spacing));
        var top = viewport.Height - margin - sunSize;

        return new Rectangle(left, top, sunSize, sunSize);
    }

    /// <summary>
    /// Sun <paramref name="index"/>'s destination rectangle for the AI (top-right) meter, whose row
    /// grows leftward from the right edge so it reads right-anchored regardless of viewport width.
    /// </summary>
    public static Rectangle GetAiSunRect(int index, Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var sunSize = (int)(minDimension * _sunSizeFraction);
        var spacing = (int)(minDimension * _sunSpacingFraction);
        var margin = (int)(minDimension * _marginFraction);

        var right = viewport.Width - margin - sunSize - (index * (sunSize + spacing));

        return new Rectangle(right, margin, sunSize, sunSize);
    }

    /// <summary>
    /// Draws all 5 suns of one player's meter - filled solid in <paramref name="ownerColor"/> for
    /// suns below <paramref name="level"/>, outlined/dimmed in <see cref="_unfilledColor"/> above it.
    /// Reuses the existing circle texture rather than adding a content-pipeline asset.
    /// </summary>
    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D circleTexture,
        Viewport viewport,
        int level,
        Color ownerColor,
        bool isHuman)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(circleTexture);

        for (var i = 0; i < _sunCount; i++)
        {
            var rect = isHuman ? GetHumanSunRect(i, viewport) : GetAiSunRect(i, viewport);
            var filled = i < level;
            var color = filled ? ownerColor : _unfilledColor;
            spriteBatch.Draw(circleTexture, rect, color);
        }
    }
}
