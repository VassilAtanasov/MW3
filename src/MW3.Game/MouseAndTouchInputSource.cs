using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace MW3.Game;

/// <summary>
/// The real <see cref="IInputSource"/>: wraps <see cref="Mouse"/>/<see cref="TouchPanel"/> for the
/// pointer, the keyboard's Escape key for desktop back requests, and <see cref="RequestBack"/> for
/// a platform back button the host had to intercept itself - confirmed on a physical device that
/// MonoGame does not surface Android's hardware back button through <see cref="Keyboard"/> here, so
/// the Android head's activity relays it in explicitly rather than this class polling for it.
/// </summary>
internal sealed class MouseAndTouchInputSource : IInputSource
{
    private KeyboardState _previousKeyboardState;
    private bool _pendingBackRequest;

    public Point PointerPosition { get; private set; }

    public bool IsPointerPressed { get; private set; }

    public bool BackRequested { get; private set; }

    /// <summary>Called by a platform head that intercepts its own back button (e.g. Android's).</summary>
    public void RequestBack()
    {
        _pendingBackRequest = true;
    }

    public void Update(Viewport viewport)
    {
        var touches = TouchPanel.GetState();
        if (touches.Count > 0)
        {
            var touch = touches[0];
            PointerPosition = touch.Position.ToPoint();
            IsPointerPressed = touch.State is TouchLocationState.Pressed or TouchLocationState.Moved;
        }
        else
        {
            var mouse = Mouse.GetState();
            PointerPosition = mouse.Position;
            IsPointerPressed = mouse.LeftButton == ButtonState.Pressed;
        }

        var keyboard = Keyboard.GetState();
        BackRequested = IsKeyNewlyDown(keyboard, Keys.Escape) || _pendingBackRequest;
        _pendingBackRequest = false;
        _previousKeyboardState = keyboard;
    }

    private bool IsKeyNewlyDown(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
}
