using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace MW3.Game;

/// <summary>
/// The welcome screen: the game title and a single inert "Play" button. Positions and sizes are
/// derived from the current viewport, so layout stays centred at any window size or device
/// aspect ratio.
/// </summary>
public sealed class WelcomeScreen : IDisposable
{
    private const string _title = "MW3";
    private const string _buttonLabel = "Play";
    private const float _referenceViewportWidth = 1280f;

    private SpriteFont? _font;
    private Texture2D? _buttonTexture;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");

        _buttonTexture = new Texture2D(graphicsDevice, 1, 1);
        _buttonTexture.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Checks for a click or tap on the Play button. Deliberately does nothing when one occurs -
    /// the button is inert by design, not by omission; navigation arrives in a later feature.
    /// </summary>
    public void Update(Viewport viewport)
    {
        if (_font is null)
        {
            return;
        }

        if (IsPointerPressed(GetButtonBounds(viewport)))
        {
            // Intentionally inert.
        }
    }

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_font is null || _buttonTexture is null)
        {
            return;
        }

        var scale = viewport.Width / _referenceViewportWidth;

        var titleSize = _font.MeasureString(_title) * scale;
        var titlePosition = new Vector2(
            (viewport.Width - titleSize.X) / 2f,
            (viewport.Height * 0.3f) - (titleSize.Y / 2f));
        spriteBatch.DrawString(_font, _title, titlePosition, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

        var buttonBounds = GetButtonBounds(viewport);
        spriteBatch.Draw(_buttonTexture, buttonBounds, Color.White);

        var labelSize = _font.MeasureString(_buttonLabel) * scale;
        var labelPosition = new Vector2(
            buttonBounds.X + ((buttonBounds.Width - labelSize.X) / 2f),
            buttonBounds.Y + ((buttonBounds.Height - labelSize.Y) / 2f));
        spriteBatch.DrawString(_font, _buttonLabel, labelPosition, Color.CornflowerBlue, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void Dispose()
    {
        _buttonTexture?.Dispose();
    }

    private static Rectangle GetButtonBounds(Viewport viewport)
    {
        var scale = viewport.Width / _referenceViewportWidth;
        var width = (int)(240 * scale);
        var height = (int)(64 * scale);
        var x = (viewport.Width - width) / 2;
        var y = (int)(viewport.Height * 0.55f);
        return new Rectangle(x, y, width, height);
    }

    private static bool IsPointerPressed(Rectangle bounds)
    {
        var mouse = Mouse.GetState();
        if (mouse.LeftButton == ButtonState.Pressed && bounds.Contains(mouse.Position))
        {
            return true;
        }

        var touches = TouchPanel.GetState();
        for (var i = 0; i < touches.Count; i++)
        {
            if (touches[i].State == TouchLocationState.Pressed && bounds.Contains(touches[i].Position))
            {
                return true;
            }
        }

        return false;
    }
}
