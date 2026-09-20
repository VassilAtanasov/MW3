using Microsoft.Xna.Framework;

namespace MW3.Game;

/// <summary>
/// Headless rasterizer for the marching-unit sprite (match-presentation FR-3). One silhouette faces
/// right (+X) so <see cref="MatchScreen"/> can rotate it toward the army's destination at draw time.
/// The body is white for owner tinting; the detail layer carries the outline and facial features.
/// </summary>
internal static class UnitSpriteRasterizer
{
    public const int Size = 48;

    private static readonly Color _outline = new(28, 24, 32, 230);

    private static readonly Color _eye = new(36, 40, 58, 240);

    /// <summary>
    /// Fills <paramref name="body"/> and <paramref name="detail"/> with a small character: round head,
    /// compact torso, two feet. Both buffers must be <see cref="Size"/> squared.
    /// </summary>
    public static void Rasterize(Color[] body, Color[] detail)
    {
        EnsureLayer(body, nameof(body));
        EnsureLayer(detail, nameof(detail));
        Clear(body);
        Clear(detail);

        // Torso - slightly wider at the shoulders, tapering to the waist.
        FillRoundedRect(body, 0.34f, 0.46f, 0.30f, 0.28f, 0.08f, Color.White);
        FillRoundedRect(detail, 0.34f, 0.46f, 0.30f, 0.28f, 0.08f, _outline);

        // Round head, forward in the march direction (+X).
        FillEllipse(body, 0.62f, 0.30f, 0.16f, 0.16f, Color.White);
        FillEllipse(detail, 0.62f, 0.30f, 0.16f, 0.16f, _outline);

        // A single visible eye on the forward face reads as a character, not a pip.
        FillEllipse(detail, 0.70f, 0.28f, 0.035f, 0.04f, _eye);

        // Feet planted on the baseline.
        FillEllipse(body, 0.40f, 0.82f, 0.08f, 0.05f, Color.White);
        FillEllipse(body, 0.56f, 0.82f, 0.08f, 0.05f, Color.White);
        FillEllipse(detail, 0.40f, 0.82f, 0.08f, 0.05f, _outline);
        FillEllipse(detail, 0.56f, 0.82f, 0.08f, 0.05f, _outline);
    }

    private static void EnsureLayer(Color[] pixels, string name)
    {
        if (pixels is null)
        {
            throw new ArgumentNullException(name);
        }

        if (pixels.Length != Size * Size)
        {
            throw new ArgumentException($"Expected {Size * Size} pixels.", name);
        }
    }

    private static void Clear(Color[] dest)
    {
        Array.Clear(dest, 0, dest.Length);
    }

    private static void FillEllipse(Color[] dest, float cx, float cy, float rx, float ry, Color color)
    {
        var minX = Math.Max(0, (int)MathF.Floor((cx - rx) * Size) - 1);
        var maxX = Math.Min(Size - 1, (int)MathF.Ceiling((cx + rx) * Size) + 1);
        var minY = Math.Max(0, (int)MathF.Floor((cy - ry) * Size) - 1);
        var maxY = Math.Min(Size - 1, (int)MathF.Ceiling((cy + ry) * Size) + 1);

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                var dx = ((px + 0.5f) / Size - cx) / rx;
                var dy = ((py + 0.5f) / Size - cy) / ry;
                var d = MathF.Sqrt((dx * dx) + (dy * dy));
                Stamp(dest, px, py, color, CoverageFromDistance(d - 1f));
            }
        }
    }

    private static void FillRoundedRect(
        Color[] dest,
        float x,
        float y,
        float w,
        float h,
        float cornerRadius,
        Color color)
    {
        FillRect(dest, x + cornerRadius, y, w - (cornerRadius * 2f), h, color);
        FillRect(dest, x, y + cornerRadius, w, h - (cornerRadius * 2f), color);
        FillEllipse(dest, x + cornerRadius, y + cornerRadius, cornerRadius, cornerRadius, color);
        FillEllipse(dest, x + w - cornerRadius, y + cornerRadius, cornerRadius, cornerRadius, color);
        FillEllipse(dest, x + cornerRadius, y + h - cornerRadius, cornerRadius, cornerRadius, color);
        FillEllipse(dest, x + w - cornerRadius, y + h - cornerRadius, cornerRadius, cornerRadius, color);
    }

    private static void FillRect(Color[] dest, float x, float y, float w, float h, Color color)
    {
        var minX = Math.Max(0, (int)MathF.Floor(x * Size));
        var maxX = Math.Min(Size - 1, (int)MathF.Ceiling((x + w) * Size) - 1);
        var minY = Math.Max(0, (int)MathF.Floor(y * Size));
        var maxY = Math.Min(Size - 1, (int)MathF.Ceiling((y + h) * Size) - 1);

        for (var py = minY; py <= maxY; py++)
        {
            for (var px = minX; px <= maxX; px++)
            {
                Stamp(dest, px, py, color, 1f);
            }
        }
    }

    private static float CoverageFromDistance(float signedDistance) =>
        Math.Clamp(0.5f - signedDistance, 0f, 1f);

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
