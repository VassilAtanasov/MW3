using Microsoft.Xna.Framework;

namespace MW3.Game;

/// <summary>
/// Headless rasterizer for the illustrated building sprites (match-presentation FR-2). It decides
/// nothing about the match: the caller already has a <see cref="BaseSnapshot"/>, and this only
/// turns that snapshot's existing <see cref="BaseSnapshot.Type"/> and <see cref="BaseSnapshot.Level"/>
/// into pixels. No new wire field is introduced.
/// <para>
/// Each building is two layers so owner is a palette on the same family. The body is white (tinted
/// at draw time by the owner colour, or grey for unowned). The detail overlay carries outlines,
/// windows, masonry and fittings in fixed colours so a tint cannot wash them out. A village grows
/// by stamping extra dwellings - extra caps - not by recolouring or scaling a single glyph.
/// </para>
/// </summary>
internal static class BuildingSpriteRasterizer
{
    public const int Size = 128;

    public const int VillageMinLevel = 1;

    public const int VillageMaxLevel = 5;

    private static readonly Color _outline = new(28, 24, 32, 230);

    private static readonly Color _windowPane = new(36, 40, 58, 240);

    private static readonly Color _windowShine = new(220, 228, 240, 220);

    private static readonly Color _masonryLine = new(40, 34, 32, 150);

    private static readonly Color _crystalFacet = new(186, 230, 240, 210);

    private static readonly Color _metal = new(48, 50, 56, 230);

    /// <summary>
    /// Fills <paramref name="body"/> and <paramref name="detail"/> for the building
    /// <paramref name="type"/> at <paramref name="level"/>. Village levels outside
    /// <see cref="VillageMinLevel"/>..<see cref="VillageMaxLevel"/> clamp to that range so a
    /// renderer never invents a sixth silhouette; tower and forge ignore level (one silhouette
    /// each). Both buffers must be <c>Size * Size</c>.
    /// </summary>
    public static void Rasterize(BaseType type, int level, Color[] body, Color[] detail)
    {
        EnsureLayer(body, nameof(body));
        EnsureLayer(detail, nameof(detail));
        Clear(body);
        Clear(detail);

        switch (type)
        {
            case BaseType.Tower:
                DrawTower(body, detail);
                break;
            case BaseType.Forge:
                DrawForge(body, detail);
                break;
            default:
                DrawVillage(Math.Clamp(level, VillageMinLevel, VillageMaxLevel), body, detail);
                break;
        }
    }

    /// <summary>
    /// A white thought-cloud with a downward tail, sized to sit above a building sprite. The
    /// garrison digit is drawn on top by the caller, including 0.
    /// </summary>
    public static void RasterizeGarrisonCloud(Color[] pixels)
    {
        EnsureLayer(pixels, nameof(pixels));
        Clear(pixels);

        FillEllipse(pixels, 0.50f, 0.48f, 0.40f, 0.26f, Color.White);
        FillEllipse(pixels, 0.32f, 0.44f, 0.18f, 0.16f, Color.White);
        FillEllipse(pixels, 0.68f, 0.44f, 0.18f, 0.16f, Color.White);
        FillEllipse(pixels, 0.50f, 0.34f, 0.18f, 0.14f, Color.White);
        FillTriangle(pixels, 0.43f, 0.70f, 0.57f, 0.70f, 0.50f, 0.90f, Color.White);
    }

    private static void DrawVillage(int level, Color[] body, Color[] detail)
    {
        // Extra caps are extra dwellings. A higher level is a more-grown cluster, never a
        // recolour or a uniform scale of the level-1 house.
        switch (level)
        {
            case 1:
                StampDwelling(body, detail, 0.50f, 0.90f, 1.00f);
                break;
            case 2:
                StampDwelling(body, detail, 0.40f, 0.90f, 0.96f);
                StampDwelling(body, detail, 0.78f, 0.62f, 0.52f);
                break;
            case 3:
                StampDwelling(body, detail, 0.32f, 0.91f, 0.90f);
                StampDwelling(body, detail, 0.70f, 0.88f, 0.80f);
                StampDwelling(body, detail, 0.84f, 0.58f, 0.48f);
                break;
            case 4:
                StampDwelling(body, detail, 0.22f, 0.92f, 0.72f);
                StampDwelling(body, detail, 0.50f, 0.88f, 0.92f);
                StampDwelling(body, detail, 0.78f, 0.90f, 0.70f);
                StampDwelling(body, detail, 0.16f, 0.58f, 0.50f);
                break;
            default:
                StampDwelling(body, detail, 0.22f, 0.92f, 0.72f);
                StampDwelling(body, detail, 0.50f, 0.88f, 0.92f);
                StampDwelling(body, detail, 0.78f, 0.90f, 0.70f);
                StampDwelling(body, detail, 0.14f, 0.56f, 0.52f);
                StampDwelling(body, detail, 0.88f, 0.54f, 0.50f);
                break;
        }
    }

    /// <summary>
    /// One organic dwelling: cap, stem, windows, feet. <paramref name="cx"/> is the stem centre,
    /// <paramref name="groundY"/> the baseline the feet sit on, <paramref name="scale"/> the size
    /// relative to the level-1 house.
    /// </summary>
    private static void StampDwelling(Color[] body, Color[] detail, float cx, float groundY, float scale)
    {
        var stemHalfW = 0.125f * scale;
        var stemH = 0.28f * scale;
        var stemTop = groundY - (0.08f * scale) - stemH;
        var stemCx = cx;

        // Feet first so the stem overlaps their tops.
        FillEllipse(body, cx - (0.11f * scale), groundY - (0.03f * scale), 0.09f * scale, 0.055f * scale, Color.White);
        FillEllipse(body, cx + (0.11f * scale), groundY - (0.03f * scale), 0.09f * scale, 0.055f * scale, Color.White);
        FillEllipse(detail, cx - (0.11f * scale), groundY - (0.03f * scale), 0.09f * scale, 0.055f * scale, _outline);
        FillEllipse(detail, cx + (0.11f * scale), groundY - (0.03f * scale), 0.09f * scale, 0.055f * scale, _outline);
        FillEllipse(body, cx - (0.11f * scale), groundY - (0.035f * scale), 0.07f * scale, 0.040f * scale, Color.White);
        FillEllipse(body, cx + (0.11f * scale), groundY - (0.035f * scale), 0.07f * scale, 0.040f * scale, Color.White);

        FillRect(body, stemCx - stemHalfW, stemTop, stemHalfW * 2f, stemH, Color.White);
        StrokeRect(detail, stemCx - stemHalfW, stemTop, stemHalfW * 2f, stemH, _outline);

        var doorW = 0.08f * scale;
        var doorH = 0.12f * scale;
        FillRect(detail, stemCx - (doorW / 2f), groundY - (0.08f * scale) - doorH, doorW, doorH, _outline);

        var windowY = stemTop + (0.06f * scale);
        var windowW = 0.055f * scale;
        var windowH = 0.07f * scale;
        StampWindow(detail, stemCx - (0.07f * scale), windowY, windowW, windowH);
        StampWindow(detail, stemCx + (0.015f * scale), windowY, windowW, windowH);

        var capRy = 0.175f * scale;
        var capRx = 0.30f * scale;
        var capCy = stemTop - (capRy * 0.35f);
        FillEllipse(body, cx, capCy, capRx, capRy, Color.White);
        StrokeEllipse(detail, cx, capCy, capRx, capRy, _outline);
        FillEllipse(detail, cx - (0.06f * scale), capCy - (0.05f * scale), 0.10f * scale, 0.05f * scale, _windowShine);
        FillEllipse(detail, cx + (0.10f * scale), capCy + (0.02f * scale), 0.045f * scale, 0.032f * scale, _outline);
        FillEllipse(detail, cx - (0.12f * scale), capCy + (0.04f * scale), 0.035f * scale, 0.025f * scale, _outline);
    }

    private static void DrawTower(Color[] body, Color[] detail)
    {
        // Masonry keep: plinth, shaft, crenellations, turret, cannon. Distinctly not a dwelling.
        FillRect(body, 0.20f, 0.80f, 0.60f, 0.12f, Color.White);
        StrokeRect(detail, 0.20f, 0.80f, 0.60f, 0.12f, _outline);

        FillRect(body, 0.30f, 0.30f, 0.40f, 0.52f, Color.White);
        StrokeRect(detail, 0.30f, 0.30f, 0.40f, 0.52f, _outline);
        StampBrickCourses(detail, 0.32f, 0.34f, 0.36f, 0.44f);

        const int merlonCount = 5;
        for (var i = 0; i < merlonCount; i++)
        {
            var x = 0.30f + (i * 0.082f);
            FillRect(body, x, 0.18f, 0.055f, 0.14f, Color.White);
            StrokeRect(detail, x, 0.18f, 0.055f, 0.14f, _outline);
        }

        FillEllipse(body, 0.50f, 0.18f, 0.13f, 0.11f, Color.White);
        StrokeEllipse(detail, 0.50f, 0.18f, 0.13f, 0.11f, _outline);

        // Cannon / turret barrel, reaching the right-hand edge so the silhouette reads at a glance.
        FillEllipse(body, 0.78f, 0.18f, 0.18f, 0.045f, Color.White);
        StrokeEllipse(detail, 0.78f, 0.18f, 0.18f, 0.045f, _metal);
        FillEllipse(detail, 0.92f, 0.18f, 0.035f, 0.035f, _metal);

        FillRect(detail, 0.44f, 0.66f, 0.12f, 0.16f, _outline);
        StampWindow(detail, 0.36f, 0.40f, 0.05f, 0.08f);
        StampWindow(detail, 0.59f, 0.40f, 0.05f, 0.08f);
        StampWindow(detail, 0.475f, 0.48f, 0.05f, 0.10f);
    }

    private static void DrawForge(Color[] body, Color[] detail)
    {
        // Workshop + crystal dome + chimney + fittings: the most complex silhouette of the three.
        FillRect(body, 0.10f, 0.54f, 0.80f, 0.34f, Color.White);
        StrokeRect(detail, 0.10f, 0.54f, 0.80f, 0.34f, _outline);
        StampBrickCourses(detail, 0.14f, 0.58f, 0.52f, 0.26f);

        FillEllipse(body, 0.18f, 0.66f, 0.10f, 0.14f, Color.White);
        StrokeEllipse(detail, 0.18f, 0.66f, 0.10f, 0.14f, _outline);

        FillEllipse(body, 0.42f, 0.40f, 0.24f, 0.22f, Color.White);
        StrokeEllipse(detail, 0.42f, 0.40f, 0.24f, 0.22f, _outline);
        FillEllipse(detail, 0.36f, 0.32f, 0.08f, 0.05f, _windowShine);
        FillTriangle(detail, 0.42f, 0.22f, 0.30f, 0.48f, 0.42f, 0.50f, _crystalFacet);
        FillTriangle(detail, 0.42f, 0.22f, 0.54f, 0.48f, 0.42f, 0.50f, _crystalFacet);
        FillTriangle(detail, 0.28f, 0.38f, 0.42f, 0.22f, 0.42f, 0.40f, _crystalFacet);
        FillTriangle(detail, 0.56f, 0.38f, 0.42f, 0.22f, 0.42f, 0.40f, _crystalFacet);
        StrokeEllipse(detail, 0.42f, 0.40f, 0.14f, 0.12f, _crystalFacet);

        FillRect(body, 0.72f, 0.16f, 0.12f, 0.42f, Color.White);
        StrokeRect(detail, 0.72f, 0.16f, 0.12f, 0.42f, _outline);
        FillRect(body, 0.69f, 0.12f, 0.18f, 0.07f, Color.White);
        StrokeRect(detail, 0.69f, 0.12f, 0.18f, 0.07f, _outline);
        FillEllipse(detail, 0.78f, 0.08f, 0.05f, 0.04f, _windowShine);
        FillEllipse(detail, 0.82f, 0.05f, 0.04f, 0.03f, _windowShine);
        FillRect(detail, 0.62f, 0.28f, 0.10f, 0.025f, _metal);
        FillRect(detail, 0.60f, 0.26f, 0.03f, 0.08f, _metal);

        FillEllipse(body, 0.80f, 0.72f, 0.09f, 0.09f, Color.White);
        StrokeEllipse(detail, 0.80f, 0.72f, 0.09f, 0.09f, _metal);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * (MathF.PI / 4f);
            var tx = 0.80f + (MathF.Cos(angle) * 0.10f);
            var ty = 0.72f + (MathF.Sin(angle) * 0.10f);
            FillRect(body, tx - 0.02f, ty - 0.02f, 0.04f, 0.04f, Color.White);
            FillRect(detail, tx - 0.015f, ty - 0.015f, 0.03f, 0.03f, _metal);
        }

        FillRect(body, 0.38f, 0.82f, 0.22f, 0.07f, Color.White);
        FillRect(body, 0.45f, 0.86f, 0.08f, 0.08f, Color.White);
        StrokeRect(detail, 0.38f, 0.82f, 0.22f, 0.07f, _metal);
        StrokeRect(detail, 0.45f, 0.86f, 0.08f, 0.08f, _metal);

        StampWindow(detail, 0.26f, 0.58f, 0.07f, 0.09f);
        StampWindow(detail, 0.36f, 0.58f, 0.07f, 0.09f);
        StampWindow(detail, 0.46f, 0.58f, 0.07f, 0.09f);
        StampWindow(detail, 0.56f, 0.58f, 0.07f, 0.09f);
        StampWindow(detail, 0.14f, 0.58f, 0.06f, 0.08f);
        FillRect(detail, 0.30f, 0.74f, 0.10f, 0.12f, _outline);
        StampBrickCourses(detail, 0.70f, 0.58f, 0.16f, 0.22f);
    }

    private static void StampWindow(Color[] detail, float x, float y, float w, float h)
    {
        FillRect(detail, x, y, w, h, _windowPane);
        StrokeRect(detail, x, y, w, h, _outline);
        FillRect(detail, x + (w * 0.15f), y + (h * 0.12f), w * 0.35f, h * 0.28f, _windowShine);
    }

    private static void StampBrickCourses(Color[] detail, float x, float y, float w, float h)
    {
        const int courses = 5;
        for (var i = 1; i < courses; i++)
        {
            var yy = y + (h * i / courses);
            FillRect(detail, x, yy, w, 0.008f, _masonryLine);
        }

        FillRect(detail, x + (w * 0.33f), y, 0.008f, h, _masonryLine);
        FillRect(detail, x + (w * 0.66f), y, 0.008f, h, _masonryLine);
    }

    private static void EnsureLayer(Color[] pixels, string name)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != Size * Size)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"Layer must be {Size}x{Size} ({Size * Size} pixels)."),
                name);
        }
    }

    private static void Clear(Color[] pixels)
    {
        Array.Clear(pixels, 0, pixels.Length);
    }

    private static void FillEllipse(Color[] dest, float cx, float cy, float rx, float ry, Color color)
    {
        var size = Size;
        var pcx = cx * size;
        var pcy = cy * size;
        var prx = rx * size;
        var pry = ry * size;
        var minX = Math.Max(0, (int)MathF.Floor(pcx - prx - 1f));
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(pcx + prx + 1f));
        var minY = Math.Max(0, (int)MathF.Floor(pcy - pry - 1f));
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(pcy + pry + 1f));
        var invRx = 1f / Math.Max(prx, 0.001f);
        var invRy = 1f / Math.Max(pry, 0.001f);

        for (var y = minY; y <= maxY; y++)
        {
            var ny = (y + 0.5f - pcy) * invRy;
            for (var x = minX; x <= maxX; x++)
            {
                var nx = (x + 0.5f - pcx) * invRx;
                var d = MathF.Sqrt((nx * nx) + (ny * ny));
                Stamp(dest, x, y, color, CoverageFromDistance(d - 1f));
            }
        }
    }

    private static void StrokeEllipse(Color[] dest, float cx, float cy, float rx, float ry, Color color)
    {
        var size = Size;
        var pcx = cx * size;
        var pcy = cy * size;
        var prx = rx * size;
        var pry = ry * size;
        var minX = Math.Max(0, (int)MathF.Floor(pcx - prx - 2f));
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(pcx + prx + 2f));
        var minY = Math.Max(0, (int)MathF.Floor(pcy - pry - 2f));
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(pcy + pry + 2f));
        var invRx = 1f / Math.Max(prx, 0.001f);
        var invRy = 1f / Math.Max(pry, 0.001f);

        for (var y = minY; y <= maxY; y++)
        {
            var ny = (y + 0.5f - pcy) * invRy;
            for (var x = minX; x <= maxX; x++)
            {
                var nx = (x + 0.5f - pcx) * invRx;
                var d = MathF.Sqrt((nx * nx) + (ny * ny));
                var rim = MathF.Abs(d - 1f);
                Stamp(dest, x, y, color, CoverageFromDistance(rim - 1.2f));
            }
        }
    }

    private static void FillRect(Color[] dest, float x, float y, float w, float h, Color color)
    {
        var size = Size;
        var x0 = x * size;
        var y0 = y * size;
        var x1 = (x + w) * size;
        var y1 = (y + h) * size;
        var minX = Math.Max(0, (int)MathF.Floor(x0) - 1);
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(x1) + 1);
        var minY = Math.Max(0, (int)MathF.Floor(y0) - 1);
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(y1) + 1);
        var cx = (x0 + x1) * 0.5f;
        var cy = (y0 + y1) * 0.5f;
        var hx = (x1 - x0) * 0.5f;
        var hy = (y1 - y0) * 0.5f;

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                var dx = MathF.Abs(px + 0.5f - cx) - hx;
                var dy = MathF.Abs(py + 0.5f - cy) - hy;
                var outside = MathF.Sqrt((MathF.Max(dx, 0f) * MathF.Max(dx, 0f)) + (MathF.Max(dy, 0f) * MathF.Max(dy, 0f)));
                var inside = MathF.Min(MathF.Max(dx, dy), 0f);
                Stamp(dest, px, py, color, CoverageFromDistance(outside + inside));
            }
        }
    }

    private static void StrokeRect(Color[] dest, float x, float y, float w, float h, Color color)
    {
        const float t = 0.012f;
        FillRect(dest, x, y, w, t, color);
        FillRect(dest, x, y + h - t, w, t, color);
        FillRect(dest, x, y, t, h, color);
        FillRect(dest, x + w - t, y, t, h, color);
    }

    private static void FillTriangle(
        Color[] dest,
        float x1,
        float y1,
        float x2,
        float y2,
        float x3,
        float y3,
        Color color)
    {
        var size = Size;
        var ax = x1 * size;
        var ay = y1 * size;
        var bx = x2 * size;
        var by = y2 * size;
        var cx = x3 * size;
        var cy = y3 * size;
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))) - 1);
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))) + 1);
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))) - 1);
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))) + 1);
        var area = ((by - cy) * (ax - cx)) + ((cx - bx) * (ay - cy));
        if (MathF.Abs(area) < 0.001f)
        {
            return;
        }

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                var x = px + 0.5f;
                var y = py + 0.5f;
                var w1 = (((by - cy) * (x - cx)) + ((cx - bx) * (y - cy))) / area;
                var w2 = (((cy - ay) * (x - cx)) + ((ax - cx) * (y - cy))) / area;
                var w3 = 1f - w1 - w2;
                var inside = w1 >= 0f && w2 >= 0f && w3 >= 0f;
                if (inside)
                {
                    Stamp(dest, px, py, color, 1f);
                }
            }
        }
    }

    private static float CoverageFromDistance(float signedDistance)
    {
        return Math.Clamp(0.5f - signedDistance, 0f, 1f);
    }

    private static void Stamp(Color[] dest, int x, int y, Color color, float coverage)
    {
        if (coverage <= 0f)
        {
            return;
        }

        var a = (byte)Math.Clamp((int)((color.A * coverage) + 0.5f), 0, 255);
        if (a == 0)
        {
            return;
        }

        var i = (y * Size) + x;
        var existing = dest[i];
        if (a < existing.A)
        {
            return;
        }

        dest[i] = new Color(color.R, color.G, color.B, a);
    }
}
