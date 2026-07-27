using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The scripted <see cref="IInputSource"/>: replays committed directives instead of reading the
/// platform, so navigation is verifiable without injecting synthetic OS events (D-17).
/// </summary>
internal sealed class ScriptedInputSource : IInputSource
{
    private readonly IReadOnlyList<ScriptDirective> _directives;
    private readonly int _lastDirectiveFrame;

    private int _currentFrame = -1;

    public ScriptedInputSource(IReadOnlyList<ScriptDirective> directives)
    {
        ArgumentNullException.ThrowIfNull(directives);

        _directives = directives;

        var lastFrame = 0;
        foreach (var directive in directives)
        {
            if (directive.Frame > lastFrame)
            {
                lastFrame = directive.Frame;
            }
        }

        _lastDirectiveFrame = lastFrame;
    }

    public Point PointerPosition { get; private set; }

    public bool IsPointerPressed { get; private set; }

    public bool BackRequested { get; private set; }

    /// <summary>
    /// True once 10 frames have passed since the last directive, so playback ends on a fixed frame
    /// count rather than something that can flake on timing.
    /// </summary>
    public bool IsPlaybackComplete => _currentFrame >= _lastDirectiveFrame + 10;

    public void Update(Viewport viewport)
    {
        _currentFrame++;
        BackRequested = false;

        foreach (var directive in _directives)
        {
            if (directive.Frame != _currentFrame)
            {
                continue;
            }

            switch (directive)
            {
                case DownDirective down:
                    PointerPosition = ToPixels(down.X, down.Y, viewport);
                    IsPointerPressed = true;
                    break;
                case UpDirective up:
                    PointerPosition = ToPixels(up.X, up.Y, viewport);
                    IsPointerPressed = false;
                    break;
                case BackDirective:
                    BackRequested = true;
                    break;
                case WaitDirective:
                    // Intentionally a no-op; its frame already counts toward _lastDirectiveFrame.
                    break;
            }
        }
    }

    private static Point ToPixels(double x, double y, Viewport viewport) =>
        new((int)(x * viewport.Width), (int)(y * viewport.Height));
}
