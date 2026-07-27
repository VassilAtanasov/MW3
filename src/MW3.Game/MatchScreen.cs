using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Draws the match live: one circle per base, tinted by owner, with its rising garrison count, plus
/// the drag interaction that sends armies (FR-5). Owns a fresh <see cref="Match"/> per instance, so
/// pushing this screen always starts a new match. Presentation reads and commands write: this class
/// submits a <see cref="SendArmyCommand"/> only through <see cref="Match.Execute"/> on a completed
/// drag and never mutates match state directly.
/// </summary>
internal sealed class MatchScreen : IScreen
{
    private const float _radiusFraction = 0.15f;
    private const float _armyRadiusFraction = 0.08f;
    private const float _selectionHighlightScale = 1.35f;

    private static readonly Color _selectionHighlightColor = Color.Gold;

    private readonly Match _match = new();

    private FixedStepClock _clock = new(Match.TickDurationMilliseconds);
    private long _elapsedTicks;

    private SpriteFont? _font;
    private Texture2D? _circleTexture;

    // Garrison text is formatted only when a base's count actually changes (at most once every
    // ProductionPeriodTicks per base), not on every Draw call - frame-loop code allocates nothing
    // per frame (docs/CONVENTIONS.md).
    private string[]? _garrisonText;
    private int[]? _lastGarrisonCount;

    // An army's unit count never changes in flight (D-12), so its text is formatted once ever and
    // cached by army id rather than reformatted every frame.
    private readonly Dictionary<int, string> _armyUnitText = new();

    // Reused scratch buffer for PruneResolvedArmyText, so pruning stale cache entries allocates
    // nothing beyond its own one-time growth.
    private readonly List<int> _armyIdsToPrune = new();

    private bool _wasPointerPressed;
    private int? _selectedSourceBaseId;

    public Color BackgroundColor => Color.DarkSlateGray;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");
        _circleTexture = CreateCircleTexture(graphicsDevice, diameter: 128);

        _garrisonText = new string[_match.Bases.Count];
        _lastGarrisonCount = new int[_match.Bases.Count];
        for (var i = 0; i < _lastGarrisonCount.Length; i++)
        {
            _lastGarrisonCount[i] = -1;
        }
    }

    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (clock, ticks) = _clock.Advance(elapsedMilliseconds);
        _clock = clock;

        if (ticks > 0)
        {
            _match.Advance(ticks);
            _elapsedTicks += ticks;
            PruneResolvedArmyText();
        }

        HandleDrag(input, viewport);
    }

    /// <summary>
    /// A press starting on a base the human owns selects it as the drag source; releasing over a
    /// different base issues a <see cref="SendArmyCommand"/> for half its garrison (read at
    /// release, floored, clamped to at least 1); releasing anywhere else cancels. Selection always
    /// clears on release, so the next press starts fresh (FR-5, D-18).
    /// </summary>
    private void HandleDrag(IInputSource input, Viewport viewport)
    {
        if (input.IsPointerPressed && !_wasPointerPressed)
        {
            var point = ToNormalized(input.PointerPosition, viewport);
            var pressedBaseId = HitTester.FindBaseAt(point, _match.Bases);
            var pressedBase = pressedBaseId is int id ? FindBase(id) : null;
            _selectedSourceBaseId = pressedBase is not null && pressedBase.Owner == _match.HumanPlayer ? pressedBase.Id : null;
        }
        else if (!input.IsPointerPressed && _wasPointerPressed)
        {
            if (_selectedSourceBaseId is int sourceId)
            {
                var point = ToNormalized(input.PointerPosition, viewport);
                var targetId = HitTester.FindBaseAt(point, _match.Bases);

                if (targetId is int target && target != sourceId)
                {
                    var source = FindBase(sourceId);
                    if (source is not null && source.Owner == _match.HumanPlayer)
                    {
                        var unitCount = Math.Max(1, source.GarrisonCount / 2);
                        if (unitCount <= source.GarrisonCount)
                        {
                            _match.Execute(new SendArmyCommand(_match.HumanPlayer, sourceId, target, unitCount));
                        }
                    }
                }
            }

            _selectedSourceBaseId = null;
        }

        _wasPointerPressed = input.IsPointerPressed;
    }

    // Indexed rather than foreach: _match.Bases is IReadOnlyList<Base>, and enumerating a List<T>
    // through that interface boxes its struct enumerator on every call - not acceptable in code
    // reached from Draw (docs/CONVENTIONS.md's no-per-frame-allocation rule).
    private Base? FindBase(int id)
    {
        var bases = _match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == id)
            {
                return bases[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Drops cached unit-count text for armies no longer in flight, so <see cref="_armyUnitText"/>
    /// does not grow for the life of a match as armies resolve.
    /// </summary>
    private void PruneResolvedArmyText()
    {
        if (_armyUnitText.Count == 0)
        {
            return;
        }

        var armies = _match.ArmiesInFlight;
        foreach (var id in _armyUnitText.Keys)
        {
            var stillInFlight = false;
            for (var i = 0; i < armies.Count; i++)
            {
                if (armies[i].Id == id)
                {
                    stillInFlight = true;
                    break;
                }
            }

            if (!stillInFlight)
            {
                _armyIdsToPrune.Add(id);
            }
        }

        if (_armyIdsToPrune.Count == 0)
        {
            return;
        }

        foreach (var id in _armyIdsToPrune)
        {
            _armyUnitText.Remove(id);
        }

        _armyIdsToPrune.Clear();
    }

    private static MapPoint ToNormalized(Point pointerPosition, Viewport viewport) =>
        new((double)pointerPosition.X / viewport.Width, (double)pointerPosition.Y / viewport.Height);

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_font is null || _circleTexture is null || _garrisonText is null || _lastGarrisonCount is null)
        {
            return;
        }

        var radius = Math.Min(viewport.Width, viewport.Height) * _radiusFraction;
        var diameter = (int)(radius * 2);
        var bases = _match.Bases;

        for (var i = 0; i < bases.Count; i++)
        {
            var b = bases[i];
            var center = new Vector2((float)(b.Position.X * viewport.Width), (float)(b.Position.Y * viewport.Height));

            if (b.Id == _selectedSourceBaseId)
            {
                var highlightRadius = radius * _selectionHighlightScale;
                var highlightDiameter = (int)(highlightRadius * 2);
                var highlightDestination = new Rectangle(
                    (int)(center.X - highlightRadius), (int)(center.Y - highlightRadius), highlightDiameter, highlightDiameter);
                spriteBatch.Draw(_circleTexture, highlightDestination, _selectionHighlightColor);
            }

            var destination = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), diameter, diameter);
            spriteBatch.Draw(_circleTexture, destination, GetOwnerColor(b.Owner));

            if (_lastGarrisonCount[i] != b.GarrisonCount)
            {
                _garrisonText[i] = b.GarrisonCount.ToString(CultureInfo.InvariantCulture);
                _lastGarrisonCount[i] = b.GarrisonCount;
            }

            var garrisonText = _garrisonText[i];
            var unscaledSize = _font.MeasureString(garrisonText);
            var textScale = (diameter * 0.5f) / unscaledSize.Y;
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(center.X - (textSize.X / 2f), center.Y - (textSize.Y / 2f));
            spriteBatch.DrawString(_font, garrisonText, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }

        DrawArmiesInFlight(spriteBatch, viewport);
    }

    /// <summary>
    /// Each in-flight army is a filled circle smaller than a base, tinted by owner, positioned by
    /// interpolating source-&gt;target between its launch and arrival ticks - sitting exactly on the
    /// source at launch and exactly on the target at arrival (FR-5).
    /// </summary>
    private void DrawArmiesInFlight(SpriteBatch spriteBatch, Viewport viewport)
    {
        if (_font is null || _circleTexture is null)
        {
            return;
        }

        var armyRadius = Math.Min(viewport.Width, viewport.Height) * _armyRadiusFraction;
        var armyDiameter = (int)(armyRadius * 2);
        var armies = _match.ArmiesInFlight;

        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var source = FindBase(army.SourceBaseId);
            var target = FindBase(army.TargetBaseId);
            if (source is null || target is null)
            {
                continue;
            }

            var span = army.ArrivalTick - army.LaunchTick;
            var fraction = span > 0 ? (double)(_elapsedTicks - army.LaunchTick) / span : 1.0;
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            var x = source.Position.X + ((target.Position.X - source.Position.X) * fraction);
            var y = source.Position.Y + ((target.Position.Y - source.Position.Y) * fraction);
            var center = new Vector2((float)(x * viewport.Width), (float)(y * viewport.Height));

            var destination = new Rectangle((int)(center.X - armyRadius), (int)(center.Y - armyRadius), armyDiameter, armyDiameter);
            spriteBatch.Draw(_circleTexture, destination, GetOwnerColor(army.Owner));

            if (!_armyUnitText.TryGetValue(army.Id, out var text))
            {
                text = army.UnitCount.ToString(CultureInfo.InvariantCulture);
                _armyUnitText[army.Id] = text;
            }

            var unscaledSize = _font.MeasureString(text);
            var textScale = (armyDiameter * 0.5f) / unscaledSize.Y;
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(center.X - (textSize.X / 2f), center.Y - (textSize.Y / 2f));
            spriteBatch.DrawString(_font, text, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Writes the match's elapsed ticks, one line per base (id, owner, garrison), and one line per
    /// in-flight army (id, owner, source, target, count, launch tick, arrival tick) to
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

        foreach (var army in _match.ArmiesInFlight)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"Army {army.Id}: Owner={army.Owner.ControllerKind} Source={army.SourceBaseId} Target={army.TargetBaseId} Count={army.UnitCount} Launch={army.LaunchTick} Arrival={army.ArrivalTick}"));
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
