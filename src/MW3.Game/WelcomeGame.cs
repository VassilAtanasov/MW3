using Microsoft.Xna.Framework;

namespace MW3.Game;

/// <summary>
/// The MonoGame entry point for this phase: a window filled with a single solid clear colour and
/// nothing else. Visible content is added by a later feature.
/// </summary>
public sealed class WelcomeGame : Microsoft.Xna.Framework.Game
{
    private readonly bool _exitAfterFirstDraw;
    private readonly GraphicsDeviceManager _graphics;

    public WelcomeGame(bool exitAfterFirstDraw = false)
    {
        _exitAfterFirstDraw = exitAfterFirstDraw;
        _graphics = new GraphicsDeviceManager(this);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);

        if (_exitAfterFirstDraw)
        {
            Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _graphics.Dispose();
        }

        base.Dispose(disposing);
    }
}
