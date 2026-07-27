using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The MonoGame entry point shared by both heads: owns the screen stack (D-16) and starts on the
/// welcome screen. Supports an unattended smoke mode (one update/draw cycle then exit), scripted
/// input playback for verifying navigation without synthetic OS events (D-17), and an optional
/// screenshot mode that writes the final rendered frame to a PNG.
/// </summary>
public sealed class MW3Game : Microsoft.Xna.Framework.Game
{
    private readonly bool _exitAfterFirstDraw;
    private readonly string? _screenshotPath;
    private readonly GraphicsDeviceManager _graphics;
    private readonly ScreenManager _screenManager = new();
    private readonly IInputSource _input;
    private readonly ScriptedInputSource? _scriptedInput;

    private SpriteBatch? _spriteBatch;

    public MW3Game(bool exitAfterFirstDraw = false, string? screenshotPath = null, IReadOnlyList<ScriptDirective>? scriptDirectives = null)
    {
        _exitAfterFirstDraw = exitAfterFirstDraw;
        _screenshotPath = screenshotPath;
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";

        if (scriptDirectives is not null)
        {
            _scriptedInput = new ScriptedInputSource(scriptDirectives);
            _input = _scriptedInput;
        }
        else
        {
            _input = new MouseAndTouchInputSource();
        }
    }

    /// <summary>
    /// Relays a platform back button a head had to intercept itself (Android's activity does not
    /// get this through MonoGame's keyboard state on every device) into the real input source.
    /// A no-op during scripted playback, where the script is the only source of back requests.
    /// </summary>
    public void NotifyBackButtonPressed()
    {
        if (_input is MouseAndTouchInputSource realInput)
        {
            realInput.RequestBack();
        }
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _screenManager.LoadContent(Content, GraphicsDevice);

        // CA2000 does not see that ScreenManager takes ownership and disposes pushed screens
        // (Pop and ScreenManager.Dispose both call Dispose on them).
#pragma warning disable CA2000
        _screenManager.Push(new WelcomeScreen());
#pragma warning restore CA2000

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        var backRequestedExit = _screenManager.Update(_input, GraphicsDevice.Viewport);

        // Outside scripted playback, a back request on the last screen exits immediately - there
        // is no frame count to honour. Under --script, exiting here instead of at the documented
        // "10 frames after the last directive" point would skip that fixed-frame wait and, if
        // --screenshot was given, skip capturing it too - so scripted mode defers to Draw's
        // isFinalFrame check even when back already asked to exit.
        if (backRequestedExit && _scriptedInput is null)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var isFinalFrame = _scriptedInput?.IsPlaybackComplete ?? _exitAfterFirstDraw;

        RenderTarget2D? screenshotTarget = null;
        try
        {
            if (isFinalFrame && _screenshotPath is not null)
            {
                screenshotTarget = new RenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
                GraphicsDevice.SetRenderTarget(screenshotTarget);
            }

            // _spriteBatch is set in LoadContent, which MonoGame always calls before the first Draw.
            _screenManager.Draw(GraphicsDevice, _spriteBatch!, GraphicsDevice.Viewport);

            if (screenshotTarget is not null)
            {
                GraphicsDevice.SetRenderTarget(null);
                SaveScreenshot(screenshotTarget, _screenshotPath!);
            }
        }
        finally
        {
            screenshotTarget?.Dispose();
        }

        base.Draw(gameTime);

        if (isFinalFrame)
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
            _spriteBatch?.Dispose();
            _screenManager.Dispose();
            _graphics.Dispose();
        }

        base.Dispose(disposing);
    }
}
