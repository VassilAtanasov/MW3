using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// Placeholder match screen: a background colour distinct from the welcome screen's and one word,
/// laid out from the viewport. Wiring the FR-1 match model into this screen is FR-3's job.
/// </summary>
internal sealed class MatchScreen : IScreen
{
    private const string _label = "Match";
    private const float _referenceViewportWidth = 1280f;

    private SpriteFont? _font;

    public Color BackgroundColor => Color.DarkSlateGray;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");
    }

    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator)
    {
        // No interaction on this placeholder screen; back navigation is handled by ScreenManager.
    }

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_font is null)
        {
            return;
        }

        var scale = viewport.Width / _referenceViewportWidth;
        var labelSize = _font.MeasureString(_label) * scale;
        var labelPosition = new Vector2(
            (viewport.Width - labelSize.X) / 2f,
            (viewport.Height - labelSize.Y) / 2f);

        spriteBatch.DrawString(_font, _label, labelPosition, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void Dispose()
    {
    }
}
