using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Draws the match live: one circle per base, tinted by owner, with its rising garrison count.
/// Owns a fresh <see cref="Match"/> per instance, so pushing this screen always starts a new
/// match. Read-only - nothing here submits a command or changes ownership (FR-4/FR-5's job).
/// </summary>
internal sealed class MatchScreen : IScreen
{
    private const float _radiusFraction = 0.15f;

    private readonly Match _match = new();

    private FixedStepClock _clock = new(Match.TickDurationMilliseconds);
    private long _elapsedTicks;

    private SpriteFont? _font;
    private Texture2D? _circleTexture;

    public Color BackgroundColor => Color.DarkSlateGray;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");
        _circleTexture = CreateCircleTexture(graphicsDevice, diameter: 128);
    }

    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator, long elapsedMilliseconds)
    {
        var (clock, ticks) = _clock.Advance(elapsedMilliseconds);
        _clock = clock;

        if (ticks > 0)
        {
            _match.Advance(ticks);
            _elapsedTicks += ticks;
        }
    }

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_font is null || _circleTexture is null)
        {
            return;
        }

        var radius = Math.Min(viewport.Width, viewport.Height) * _radiusFraction;
        var diameter = (int)(radius * 2);

        foreach (var b in _match.Bases)
        {
            var center = new Vector2((float)(b.Position.X * viewport.Width), (float)(b.Position.Y * viewport.Height));
            var destination = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), diameter, diameter);
            spriteBatch.Draw(_circleTexture, destination, GetOwnerColor(b.Owner));

            var garrisonText = b.GarrisonCount.ToString(CultureInfo.InvariantCulture);
            var unscaledSize = _font.MeasureString(garrisonText);
            var textScale = (diameter * 0.5f) / unscaledSize.Y;
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(center.X - (textSize.X / 2f), center.Y - (textSize.Y / 2f));
            spriteBatch.DrawString(_font, garrisonText, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Writes the match's elapsed ticks and one line per base (id, owner, garrison) to
    /// <paramref name="path"/>, for `--dump-state` to give QA exact numbers instead of pixels.
    /// </summary>
    internal void WriteStateDump(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(FormattableString.Invariant($"ElapsedTicks: {_elapsedTicks}"));

        foreach (var b in _match.Bases)
        {
            var owner = b.Owner?.ControllerKind.ToString() ?? "Neutral";
            writer.WriteLine(FormattableString.Invariant($"Base {b.Id}: Owner={owner} Garrison={b.GarrisonCount}"));
        }
    }

    public void Dispose()
    {
        _circleTexture?.Dispose();
    }

    private static Color GetOwnerColor(Player? owner)
    {
        if (owner is null)
        {
            return Color.Gray;
        }

        return owner.ControllerKind switch
        {
            PlayerControllerKind.Human => Color.RoyalBlue,
            PlayerControllerKind.Ai => Color.Firebrick,
            _ => Color.Gray,
        };
    }

    private static Texture2D CreateCircleTexture(GraphicsDevice graphicsDevice, int diameter)
    {
        var texture = new Texture2D(graphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        var radius = diameter / 2f;
        var center = new Vector2(radius, radius);

        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                data[(y * diameter) + x] = distance <= radius ? Color.White : Color.Transparent;
            }
        }

        texture.SetData(data);
        return texture;
    }
}
