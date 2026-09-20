using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// GPU textures for FR-2's illustrated buildings, created once at load from
/// <see cref="BuildingSpriteRasterizer"/> and reused every frame. The village family is five
/// textures (one silhouette per existing snapshot level); tower and forge are one each. Owner
/// colour is a <see cref="SpriteBatch"/> tint on the white body, not a third set of textures.
/// </summary>
internal sealed class BuildingSpriteAtlas : IDisposable
{
    private readonly Texture2D[] _villageBody;
    private readonly Texture2D[] _villageDetail;
    private readonly Texture2D _towerBody;
    private readonly Texture2D _towerDetail;
    private readonly Texture2D _forgeBody;
    private readonly Texture2D _forgeDetail;
    private bool _disposed;

    private BuildingSpriteAtlas(
        Texture2D[] villageBody,
        Texture2D[] villageDetail,
        Texture2D towerBody,
        Texture2D towerDetail,
        Texture2D forgeBody,
        Texture2D forgeDetail,
        Texture2D cloud)
    {
        _villageBody = villageBody;
        _villageDetail = villageDetail;
        _towerBody = towerBody;
        _towerDetail = towerDetail;
        _forgeBody = forgeBody;
        _forgeDetail = forgeDetail;
        Cloud = cloud;
    }

    public Texture2D Cloud { get; }

    public static BuildingSpriteAtlas Create(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var villageBody = new Texture2D[BuildingSpriteRasterizer.VillageMaxLevel];
        var villageDetail = new Texture2D[BuildingSpriteRasterizer.VillageMaxLevel];
        var body = new Color[BuildingSpriteRasterizer.Size * BuildingSpriteRasterizer.Size];
        var detail = new Color[BuildingSpriteRasterizer.Size * BuildingSpriteRasterizer.Size];
        var uploaded = new List<Texture2D>();

        try
        {
            for (var level = BuildingSpriteRasterizer.VillageMinLevel;
                 level <= BuildingSpriteRasterizer.VillageMaxLevel;
                 level++)
            {
                BuildingSpriteRasterizer.Rasterize(BaseType.Producer, level, body, detail);
                villageBody[level - 1] = Upload(graphicsDevice, body, uploaded);
                villageDetail[level - 1] = Upload(graphicsDevice, detail, uploaded);
            }

            BuildingSpriteRasterizer.Rasterize(BaseType.Tower, level: 1, body, detail);
            var towerBody = Upload(graphicsDevice, body, uploaded);
            var towerDetail = Upload(graphicsDevice, detail, uploaded);

            BuildingSpriteRasterizer.Rasterize(BaseType.Forge, level: 1, body, detail);
            var forgeBody = Upload(graphicsDevice, body, uploaded);
            var forgeDetail = Upload(graphicsDevice, detail, uploaded);

            var cloudPixels = new Color[BuildingSpriteRasterizer.Size * BuildingSpriteRasterizer.Size];
            BuildingSpriteRasterizer.RasterizeGarrisonCloud(cloudPixels);

            return new BuildingSpriteAtlas(
                villageBody,
                villageDetail,
                towerBody,
                towerDetail,
                forgeBody,
                forgeDetail,
                Upload(graphicsDevice, cloudPixels, uploaded));
        }
        catch
        {
            for (var i = 0; i < uploaded.Count; i++)
            {
                uploaded[i].Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// The body (white, for owner tint) and detail overlay for this snapshot type and level.
    /// Village level is the existing <see cref="BaseSnapshot.Level"/>; out-of-range values clamp
    /// to the five-house family rather than allocating a new sprite.
    /// </summary>
    public void GetLayers(BaseType type, int level, out Texture2D body, out Texture2D detail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        switch (type)
        {
            case BaseType.Tower:
                body = _towerBody;
                detail = _towerDetail;
                return;
            case BaseType.Forge:
                body = _forgeBody;
                detail = _forgeDetail;
                return;
            default:
                var index = Math.Clamp(
                    level,
                    BuildingSpriteRasterizer.VillageMinLevel,
                    BuildingSpriteRasterizer.VillageMaxLevel) - 1;
                body = _villageBody[index];
                detail = _villageDetail[index];
                return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeAll(_villageBody);
        DisposeAll(_villageDetail);
        _towerBody.Dispose();
        _towerDetail.Dispose();
        _forgeBody.Dispose();
        _forgeDetail.Dispose();
        Cloud.Dispose();
    }

    private static Texture2D Upload(GraphicsDevice graphicsDevice, Color[] pixels, List<Texture2D> uploaded)
    {
        var texture = new Texture2D(graphicsDevice, BuildingSpriteRasterizer.Size, BuildingSpriteRasterizer.Size);
        uploaded.Add(texture);
        texture.SetData(pixels);
        return texture;
    }

    private static void DisposeAll(Texture2D[] textures)
    {
        for (var i = 0; i < textures.Length; i++)
        {
            textures[i].Dispose();
        }
    }
}
