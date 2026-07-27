using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// The shape every screen has (D-16): the shared `LoadContent`/`Update`/`Draw`/`Dispose` lifecycle
/// that <see cref="ScreenManager"/> routes to whichever screen is on top of the stack.
/// </summary>
internal interface IScreen : IDisposable
{
    Color BackgroundColor { get; }

    void LoadContent(ContentManager content, GraphicsDevice graphicsDevice);

    void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator);

    void Draw(SpriteBatch spriteBatch, Viewport viewport);
}
