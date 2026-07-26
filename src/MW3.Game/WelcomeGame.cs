using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The MonoGame entry point shared by both heads: shows the welcome screen. Supports an
/// unattended smoke mode that runs one update/draw cycle and exits, and an optional screenshot
/// mode that writes the rendered frame to a PNG for unattended visual verification.
/// </summary>
public sealed class WelcomeGame : Microsoft.Xna.Framework.Game
{
    private readonly bool _exitAfterFirstDraw;
    private readonly string? _screenshotPath;
    private readonly GraphicsDeviceManager _graphics;
    private readonly WelcomeScreen _screen = new();

    private SpriteBatch? _spriteBatch;
    private RenderTarget2D? _screenshotTarget;

    public WelcomeGame(bool exitAfterFirstDraw = false, string? screenshotPath = null)
    {
        _exitAfterFirstDraw = exitAfterFirstDraw;
        _screenshotPath = screenshotPath;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _screen.LoadContent(Content, GraphicsDevice);

        if (_screenshotPath is not null)
        {
            _screenshotTarget = new RenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        }

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        _screen.Update(GraphicsDevice.Viewport);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_screenshotTarget is not null)
        {
            GraphicsDevice.SetRenderTarget(_screenshotTarget);
        }

        GraphicsDevice.Clear(Color.CornflowerBlue);

        // _spriteBatch is set in LoadContent, which MonoGame always calls before the first Draw.
        _spriteBatch!.Begin();
        _screen.Draw(_spriteBatch, GraphicsDevice.Viewport);
        _spriteBatch.End();

        if (_screenshotTarget is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            SaveScreenshot(_screenshotTarget, _screenshotPath!);
        }

        base.Draw(gameTime);

        if (_exitAfterFirstDraw)
        {
            Exit();
        }
    }

    private static void SaveScreenshot(RenderTarget2D target, string path)
    {
        using var stream = File.Create(path);
        target.SaveAsPng(stream, target.Width, target.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshotTarget?.Dispose();
            _spriteBatch?.Dispose();
            _screen.Dispose();
            _graphics.Dispose();
        }

        base.Dispose(disposing);
    }
}
