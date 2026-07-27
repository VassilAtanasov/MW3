using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The one seam screens read pointer and back-request state through (D-17). The production
/// implementation wraps the platform APIs; a scripted implementation replays a committed file, so
/// interactive navigation is verifiable without injecting synthetic OS events.
/// </summary>
internal interface IInputSource
{
    Point PointerPosition { get; }

    bool IsPointerPressed { get; }

    bool BackRequested { get; }

    void Update(Viewport viewport);
}
