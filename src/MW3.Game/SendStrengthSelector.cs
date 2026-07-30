using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// A persistent bottom-left vertical strip of four buttons - 25/50/75/100, stacked upward with 25
/// nearest the bottom edge - that lets the player choose a <see cref="SendStrength"/> before
/// dragging to send. Owns its own layout, drawing, and hit-testing, and decides nothing itself
/// (D-25, the same division <see cref="BaseActionMenu"/> follows). The selection is a standing
/// mode, not a per-drag modifier (D-34): it persists across sends until the player picks a
/// different one, and defaults to <see cref="SendStrength.Half"/> so an untouched send stays
/// bit-identical to before this feature.
/// </summary>
internal sealed class SendStrengthSelector
{
    // Ordered bottom-to-top: index 0 (nearest the bottom edge, the repeatedly-tapped snaking
    // option) is Quarter, index 3 (topmost) is Full.
    private static readonly SendStrength[] _strengths =
    {
        SendStrength.Quarter, SendStrength.Half, SendStrength.ThreeQuarters, SendStrength.Full,
    };

    private const float _buttonSizeFraction = 0.08f;
    private const float _marginFraction = 0.02f;
    private const float _spacingFraction = 0.012f;

    private static readonly Color _selectedColor = Color.DarkGoldenrod;
    private static readonly Color _unselectedColor = Color.DimGray;

    public SendStrength SelectedStrength { get; private set; } = SendStrength.Half;

    /// <summary>
    /// The button index at <paramref name="normalizedPoint"/>, or null if it falls outside every
    /// button - used both to gate activation and to keep a press on the control from ever falling
    /// through to base selection.
    /// </summary>
    public static int? HitTestButton(MapPoint normalizedPoint, Viewport viewport)
    {
        var pixel = new Point((int)(normalizedPoint.X * viewport.Width), (int)(normalizedPoint.Y * viewport.Height));

        for (var i = 0; i < _strengths.Length; i++)
        {
            if (GetButtonRect(i, viewport).Contains(pixel))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets the selection to <paramref name="buttonIndex"/>'s strength unconditionally - the caller
    /// only calls this once, on a release matching a press that began on this button, exactly as
    /// <see cref="BaseActionMenu.Activate"/> is called for its own buttons.
    /// </summary>
    public void Activate(int buttonIndex)
    {
        if (buttonIndex < 0 || buttonIndex >= _strengths.Length)
        {
            return;
        }

        SelectedStrength = _strengths[buttonIndex];
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D buttonTexture, SpriteFont font, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(buttonTexture);
        ArgumentNullException.ThrowIfNull(font);

        for (var i = 0; i < _strengths.Length; i++)
        {
            var rect = GetButtonRect(i, viewport);
            var color = _strengths[i] == SelectedStrength ? _selectedColor : _unselectedColor;
            spriteBatch.Draw(buttonTexture, rect, color);

            var label = ((int)_strengths[i]).ToString(CultureInfo.InvariantCulture);
            var unscaledSize = font.MeasureString(label);
            var textScale = Math.Min((rect.Width * 0.7f) / unscaledSize.X, (rect.Height * 0.6f) / unscaledSize.Y);
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(
                rect.X + ((rect.Width - textSize.X) / 2f),
                rect.Y + ((rect.Height - textSize.Y) / 2f));
            spriteBatch.DrawString(font, label, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Button <paramref name="index"/>'s destination rectangle, sized and spaced as fractions of the
    /// viewport's smaller dimension, anchored with a margin from the left and bottom edges - index 0
    /// sits lowest, each following index stacked directly above the last.
    /// </summary>
    private static Rectangle GetButtonRect(int index, Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var buttonSize = (int)(minDimension * _buttonSizeFraction);
        var margin = (int)(minDimension * _marginFraction);
        var spacing = (int)(minDimension * _spacingFraction);

        var left = margin;
        var bottom = viewport.Height - margin - (index * (buttonSize + spacing));
        var top = bottom - buttonSize;

        return new Rectangle(left, top, buttonSize, buttonSize);
    }
}
