using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The welcome screen: the game title and a "Play" button that pushes the match screen. Positions
/// and sizes are derived from the current viewport, so layout stays centred at any window size or
/// device aspect ratio.
/// </summary>
internal sealed class WelcomeScreen : IScreen
{
    private const string _title = "MW3";
    private const string _buttonLabel = "Play";
    private const float _referenceViewportWidth = 1280f;

    private SpriteFont? _font;
    private Texture2D? _buttonTexture;
    private bool _wasPointerPressed;
    private bool _pressStartedInsideButton;

    public Color BackgroundColor => Color.CornflowerBlue;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");

        _buttonTexture = new Texture2D(graphicsDevice, 1, 1);
        _buttonTexture.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Activates on release: the press must start and end within the button's bounds, so pressing
    /// inside and dragging off before releasing does not navigate.
    /// </summary>
    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(navigator);

        if (_font is null)
        {
            return;
        }

        var isInsideNow = GetButtonBounds(viewport).Contains(input.PointerPosition);

        if (input.IsPointerPressed && !_wasPointerPressed)
        {
            _pressStartedInsideButton = isInsideNow;
        }
        else if (!input.IsPointerPressed && _wasPointerPressed)
        {
            if (_pressStartedInsideButton && isInsideNow)
            {
                // CA2000 does not see that ScreenManager (the navigator) takes ownership and
                // disposes pushed screens (Pop and ScreenManager.Dispose both call Dispose).
#pragma warning disable CA2000
                navigator.Push(new MatchScreen());
#pragma warning restore CA2000
            }

            _pressStartedInsideButton = false;
        }

        _wasPointerPressed = input.IsPointerPressed;
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
}
