using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// GPU textures for FR-3's marching unit sprite, created once at load from
/// <see cref="UnitSpriteRasterizer"/> and reused every frame. Owner colour is a
/// <see cref="SpriteBatch"/> tint on the white body.
/// </summary>
internal sealed class UnitSpriteAtlas : IDisposable
{
    private readonly Texture2D _body;
    private readonly Texture2D _detail;
    private bool _disposed;

    private UnitSpriteAtlas(Texture2D body, Texture2D detail)
    {
        _body = body;
        _detail = detail;
    }

    public Texture2D Body
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _body;
        }
    }

    public Texture2D Detail
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _detail;
        }
    }

    public static UnitSpriteAtlas Create(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var body = new Color[UnitSpriteRasterizer.Size * UnitSpriteRasterizer.Size];
        var detail = new Color[UnitSpriteRasterizer.Size * UnitSpriteRasterizer.Size];
        UnitSpriteRasterizer.Rasterize(body, detail);

        var bodyTexture = new Texture2D(graphicsDevice, UnitSpriteRasterizer.Size, UnitSpriteRasterizer.Size);
        var detailTexture = new Texture2D(graphicsDevice, UnitSpriteRasterizer.Size, UnitSpriteRasterizer.Size);
        try
        {
            bodyTexture.SetData(body);
            detailTexture.SetData(detail);
        }
        catch
        {
            bodyTexture.Dispose();
            detailTexture.Dispose();
            throw;
        }

        return new UnitSpriteAtlas(bodyTexture, detailTexture);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _body.Dispose();
        _detail.Dispose();
    }
}
