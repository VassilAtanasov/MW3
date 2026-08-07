using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// The welcome screen: the game title and three map buttons - Small, Medium, Big - each pushing a
/// match on that map (FR-2). Positions and sizes are derived from the current viewport, so layout
/// stays centred at any window size or device aspect ratio.
/// </summary>
internal sealed class WelcomeScreen : IScreen
{
    private const string _title = "MW3";
    private const float _referenceViewportWidth = 1280f;

    // Stacked from today's single-button y (0.55 * viewport height) with a 24-unit reference gap
    // between buttons (D-56's kickoff). Small therefore occupies exactly the position Play occupied.
    private static readonly (MapId Id, string Label)[] _buttons =
    {
        (MapId.Small, "Small"),
        (MapId.Medium, "Medium"),
        (MapId.Big, "Big"),
    };

    private SpriteFont? _font;
    private Texture2D? _buttonTexture;
    private bool _wasPointerPressed;
    private int _pressStartedInsideButtonIndex = -1;

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
    /// Activates on release: the press must start and end within the same button's bounds, so
    /// pressing inside one and releasing over another - or off every button - navigates nowhere.
    /// </summary>
    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(navigator);

        if (_font is null)
        {
            return;
        }

        var insideIndex = IndexOfButtonContaining(viewport, input.PointerPosition);

        if (input.IsPointerPressed && !_wasPointerPressed)
        {
            _pressStartedInsideButtonIndex = insideIndex;
        }
        else if (!input.IsPointerPressed && _wasPointerPressed)
        {
            if (_pressStartedInsideButtonIndex >= 0 && _pressStartedInsideButtonIndex == insideIndex)
            {
                // CA2000 does not see that ScreenManager (the navigator) takes ownership and
                // disposes pushed screens (Pop and ScreenManager.Dispose both call Dispose).
#pragma warning disable CA2000
                navigator.Push(new MatchScreen(MapCatalog.Get(_buttons[_pressStartedInsideButtonIndex].Id)));
#pragma warning restore CA2000
            }

            _pressStartedInsideButtonIndex = -1;
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

        for (var i = 0; i < _buttons.Length; i++)
        {
            var buttonBounds = GetButtonBounds(viewport, i);
            spriteBatch.Draw(_buttonTexture, buttonBounds, Color.White);

            var label = _buttons[i].Label;
            var labelSize = _font.MeasureString(label) * scale;
            var labelPosition = new Vector2(
                buttonBounds.X + ((buttonBounds.Width - labelSize.X) / 2f),
                buttonBounds.Y + ((buttonBounds.Height - labelSize.Y) / 2f));
            spriteBatch.DrawString(_font, label, labelPosition, Color.CornflowerBlue, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }

    public void Dispose()
    {
        _buttonTexture?.Dispose();
    }

    private static int IndexOfButtonContaining(Viewport viewport, Point position)
    {
        for (var i = 0; i < _buttons.Length; i++)
        {
            if (GetButtonBounds(viewport, i).Contains(position))
            {
                return i;
            }
        }

        return -1;
    }

    private static Rectangle GetButtonBounds(Viewport viewport, int index)
    {
        var scale = viewport.Width / _referenceViewportWidth;
        var width = (int)(240 * scale);
        var height = (int)(64 * scale);
        var gap = (int)(24 * scale);
        var x = (viewport.Width - width) / 2;
        var y = (int)(viewport.Height * 0.55f) + (index * (height + gap));
        return new Rectangle(x, y, width, height);
    }
}
