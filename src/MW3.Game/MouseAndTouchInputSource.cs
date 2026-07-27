using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace MW3.Game;

/// <summary>
/// The real <see cref="IInputSource"/>: wraps <see cref="Mouse"/>/<see cref="TouchPanel"/> for the
/// pointer and the keyboard for back requests. On Android, MonoGame maps the hardware back button
/// to <see cref="Keys.Back"/>, so one code path covers desktop Escape and the device back button.
/// </summary>
internal sealed class MouseAndTouchInputSource : IInputSource
{
    private KeyboardState _previousKeyboardState;

    public Point PointerPosition { get; private set; }

    public bool IsPointerPressed { get; private set; }

    public bool BackRequested { get; private set; }

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
        BackRequested = IsKeyNewlyDown(keyboard, Keys.Escape) || IsKeyNewlyDown(keyboard, Keys.Back);
        _previousKeyboardState = keyboard;
    }

    private bool IsKeyNewlyDown(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
}
