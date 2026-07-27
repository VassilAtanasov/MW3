using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// Owns the current screen as a stack (D-16), so the host game class routes lifecycle calls
/// through one manager instead of naming a screen directly.
/// </summary>
internal sealed class ScreenManager : IScreenNavigator, IDisposable
{
    private readonly Stack<IScreen> _screens = new();

    private ContentManager? _content;
    private GraphicsDevice? _graphicsDevice;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _content = content;
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>The screen on top of the stack, for a host that needs to inspect it (e.g. a state dump).</summary>
    public IScreen Current => _screens.Peek();

    public void Push(IScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (_content is null || _graphicsDevice is null)
        {
            throw new InvalidOperationException("LoadContent must be called before the first screen is pushed.");
        }

        screen.LoadContent(_content, _graphicsDevice);
        _screens.Push(screen);
    }

    /// <summary>
    /// Advances input and the top screen by one frame. Returns true when a back request arrived
    /// with only one screen left, meaning the host should exit the application rather than pop an
    /// empty stack.
    /// </summary>
    public bool Update(IInputSource input, Viewport viewport, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.Update(viewport);

        if (input.BackRequested)
        {
            if (_screens.Count > 1)
            {
                _screens.Pop().Dispose();
                return false;
            }

            return true;
        }

        _screens.Peek().Update(input, viewport, this, elapsedMilliseconds);
        return false;
    }

    public void Draw(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        graphicsDevice.Clear(_screens.Peek().BackgroundColor);

        spriteBatch.Begin();
        _screens.Peek().Draw(spriteBatch, viewport);
        spriteBatch.End();
    }

    public void Dispose()
    {
        while (_screens.Count > 0)
        {
            _screens.Pop().Dispose();
        }
    }
}
