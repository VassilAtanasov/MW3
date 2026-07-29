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
    private readonly string? _dumpStatePath;
    private readonly long _timeScale;
    private readonly GraphicsDeviceManager _graphics;
    private readonly ScreenManager _screenManager = new();
    private readonly IInputSource _input;
    private readonly ScriptedInputSource? _scriptedInput;

    private SpriteBatch? _spriteBatch;

    public MW3Game(bool exitAfterFirstDraw = false, string? screenshotPath = null, string? dumpStatePath = null, IReadOnlyList<ScriptDirective>? scriptDirectives = null, long timeScale = 1)
    {
        if (timeScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale), timeScale, "Time scale must be a positive integer.");
        }

        _exitAfterFirstDraw = exitAfterFirstDraw;
        _screenshotPath = screenshotPath;
        _dumpStatePath = dumpStatePath;
        _timeScale = timeScale;

        // 1280x720 matches the reference resolution every screen's layout already scales from,
        // and is one of the two resolutions FR-3's non-clipping/non-overlap criterion is checked
        // at (the other, 1920x1200, is the attached device's own screen - verified over adb).
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };

        Content.RootDirectory = "Content";

        if (scriptDirectives is not null)
        {
            _scriptedInput = new ScriptedInputSource(scriptDirectives);
            _input = _scriptedInput;

            // MonoGame's default fixed timestep can call Update() more than once per Draw() to
            // catch up on accumulated real time, or occasionally drop a step, when a frame runs
            // slow (host load, first-frame JIT/texture costs). Scripted playback counts elapsed
            // Update() calls (ScriptedInputSource._currentFrame) to know when to fire a directive
            // and when to stop, so a variable call count made two runs of the same script diverge
            // by a few ticks even after pairing every call with a fixed nominal step (below) -
            // observed as a non-byte-identical screenshot on individual re-runs of
            // qa/scripts/army-shrinking-early.txt. Disabling the fixed step for scripted runs pairs
            // Update and Draw 1:1 with no catch-up, so the call count is exactly the script's own
            // frame count and nothing else. Unscripted play is unaffected.
            IsFixedTimeStep = false;
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
        ArgumentNullException.ThrowIfNull(gameTime);

        // Screens receive only the elapsed millisecond count, never GameTime itself, so no screen
        // can reach for a wall-clock member (D-12). --time-scale (FR-7) multiplies this value only -
        // the tick sequence it produces is exactly the one real-time play would, just delivered
        // sooner; no rule or behaviour changes, only how fast it arrives.
        //
        // Under scripted playback, this reads TargetElapsedTime rather than the measured
        // gameTime.ElapsedGameTime. The two are equal in the common case (MonoGame's fixed timestep
        // targets a constant step), but MonoGame's catch-up accumulator can occasionally deliver one
        // Update call with a slightly different ElapsedGameTime under host load, which --time-scale
        // then amplifies into a several-tick discrepancy between otherwise-identical runs of the same
        // script - observed as a non-byte-identical screenshot for a script re-run individually
        // (qa/scripts/army-shrinking-early.txt). A script's frame count (how many Update calls have
        // happened) is exact regardless of that accumulator's internal bookkeeping, so anchoring
        // scripted ticks to the nominal step instead of the measured one removes the jitter without
        // touching real, unscripted play at all.
        var elapsedMilliseconds = _scriptedInput is not null
            ? (long)TargetElapsedTime.TotalMilliseconds * _timeScale
            : (long)gameTime.ElapsedGameTime.TotalMilliseconds * _timeScale;
        var backRequestedExit = _screenManager.Update(_input, GraphicsDevice.Viewport, elapsedMilliseconds);

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

            if (isFinalFrame && _dumpStatePath is not null && _screenManager.Current is MatchScreen matchScreen)
            {
                matchScreen.WriteStateDump(_dumpStatePath);
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
