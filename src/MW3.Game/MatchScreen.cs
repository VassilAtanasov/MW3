using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Draws the match live: one circle per base, tinted by owner, with its rising garrison count, plus
/// the drag interaction that sends armies (FR-5). Owns a fresh <see cref="Match"/> and a fresh
/// <see cref="MatchRunner"/> (with a fresh AI brain) per instance, so pushing this screen always
/// starts a new match against a new opponent. Presentation reads and commands write: this class
/// reads <see cref="Match"/> state directly to draw it, but advances and submits commands only
/// through <see cref="MatchRunner"/> - the runner is the one object that owns the match (FR-6).
/// </summary>
internal sealed class MatchScreen : IScreen
{
    private const float _radiusFraction = 0.15f;
    private const float _armyRadiusFraction = 0.08f;
    private const float _selectionHighlightScale = 1.35f;

    private static readonly Color _selectionHighlightColor = Color.Gold;

    private readonly Match _match = new();
    private readonly MatchRunner _runner;

    private FixedStepClock _clock = new(Match.TickDurationMilliseconds);

    private SpriteFont? _font;
    private Texture2D? _circleTexture;

    // Garrison text is formatted only when a base's count actually changes (at most once every
    // production period per base, and not at all while it sits at its cap), not on every Draw call
    // - frame-loop code allocates nothing per frame (docs/CONVENTIONS.md).
    private string[]? _garrisonText;
    private int[]? _lastGarrisonCount;

    // An army's unit count can now drop while it is in flight (FR-4, a tower shooting it down), so
    // its text is cached alongside the count it was formatted from and only reformatted when that
    // count has actually changed - not on every Draw call (docs/CONVENTIONS.md's no-per-frame-
    // allocation rule), and not assuming the premise D-12 originally stated for this cache, which
    // this feature is the one that reverses.
    private readonly Dictionary<int, (string Text, int Count)> _armyUnitText = new();

    // Reused scratch buffer for PruneResolvedArmyText, so pruning stale cache entries allocates
    // nothing beyond its own one-time growth.
    private readonly List<int> _armyIdsToPrune = new();

    private bool _wasPointerPressed;
    private int? _selectedSourceBaseId;
    private bool _pressBeganAfterOutcomeDecided;

    // The action menu is presentation state only - MW3.Core never learns it exists (D-26).
    private BaseActionMenu? _openMenu;
    private int? _pressBeganOnMenuButtonIndex;
    private bool _pressBeganOnGreyedMenuButton;
    private bool _pressDismissedMenuOnThisPress;

    private Texture2D? _buttonTexture;

    // A base's per-level ring is drawn as an enlarged, darker-tinted copy of its own fill circle -
    // no second texture, and its thickness comes from LevelTable rather than a literal here (D-22).
    private static readonly Color _ringDarkenTarget = Color.Black;
    private const float _ringDarkenAmount = 0.4f;

    // A base under construction (D-30, FR-3c) draws one further ring, outside the level ring, in a
    // fixed colour distinguishable from both the owner tint and the darkened level ring at both
    // 1280x720 and 1808x1018 - the one deliberate presentation change this feature makes.
    private static readonly Color _constructionRingColor = Color.Yellow;
    private const float _constructionRingThicknessFraction = 0.08f;

    public MatchScreen()
    {
        _runner = new MatchRunner(_match, new AiBrain(_match.AiPlayer));
    }

    public Color BackgroundColor => Color.DarkSlateGray;

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");
        _circleTexture = CreateCircleTexture(graphicsDevice, diameter: 128);
        _buttonTexture = CreateButtonTexture(graphicsDevice);

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
        ArgumentNullException.ThrowIfNull(navigator);

        if (_match.Outcome == MatchOutcome.InProgress)
        {
            var (clock, ticks) = _clock.Advance(elapsedMilliseconds);
            _clock = clock;

            if (ticks > 0)
            {
                _runner.Advance(ticks);
                PruneResolvedArmyText();
            }
        }

        if (_match.Outcome != MatchOutcome.InProgress)
        {
            // A drag in progress the moment the match ends sends nothing and shows no selection -
            // clearing here covers both "decided this very frame, mid-drag" and every frame after.
            _selectedSourceBaseId = null;
        }

        // The menu dismisses itself with no player input the moment its base stops being human-owned
        // (captured, or demoted below ownership is impossible - only capture changes owner) or the
        // outcome is decided - checked every frame, independently of any press or release.
        if (_openMenu is not null)
        {
            var anchorBase = FindBase(_openMenu.BaseId);
            if (anchorBase is null || anchorBase.Owner != _match.HumanPlayer || _match.Outcome != MatchOutcome.InProgress)
            {
                _openMenu = null;
            }
            else
            {
                _openMenu.Refresh();
            }
        }

        HandleInput(input, viewport, navigator);
    }

    /// <summary>
    /// While the match is in progress: a press starting on a base the human owns selects it as the
    /// drag source; releasing over a different base issues a <see cref="SendArmyCommand"/> for half
    /// its garrison (read at release, floored, clamped to at least 1); releasing anywhere else
    /// cancels. Selection always clears on release, so the next press starts fresh (FR-5, D-18).
    /// Once decided: no drag is possible, and a release whose press began after the decision pops
    /// back to the welcome screen - a release from a press that began before it does not, so a drag
    /// already underway when the match ended cannot skip the result the player never saw (FR-7).
    /// </summary>
    private void HandleInput(IInputSource input, Viewport viewport, IScreenNavigator navigator)
    {
        var outcomeDecided = _match.Outcome != MatchOutcome.InProgress;

        if (input.IsPointerPressed && !_wasPointerPressed)
        {
            _pressBeganAfterOutcomeDecided = outcomeDecided;
            _pressBeganOnMenuButtonIndex = null;
            _pressBeganOnGreyedMenuButton = false;
            _pressDismissedMenuOnThisPress = false;

            if (!outcomeDecided)
            {
                if (_openMenu is not null)
                {
                    HandlePressWhileMenuOpen(input, viewport);
                }
                else
                {
                    var point = ToNormalized(input.PointerPosition, viewport);
                    var pressedBaseId = HitTester.FindBaseAt(point, _match.Bases);
                    var pressedBase = pressedBaseId is int id ? FindBase(id) : null;
                    _selectedSourceBaseId = pressedBase is not null && pressedBase.Owner == _match.HumanPlayer ? pressedBase.Id : null;
                }
            }
        }
        else if (!input.IsPointerPressed && _wasPointerPressed)
        {
            if (outcomeDecided)
            {
                if (_pressBeganAfterOutcomeDecided)
                {
                    navigator.Pop();
                    _wasPointerPressed = false;
                    return;
                }
            }
            else if (_pressDismissedMenuOnThisPress)
            {
                // The down that started this gesture already dismissed a menu - its release does
                // nothing at all: no army, no highlight, no new menu (D-26).
            }
            else if (_pressBeganOnGreyedMenuButton)
            {
                // Pressing a greyed button does nothing at all: no command submitted, and the menu
                // stays open exactly as it was.
            }
            else if (_pressBeganOnMenuButtonIndex is int buttonIndex)
            {
                _openMenu?.Activate(buttonIndex, _runner);
                _openMenu = null;
            }
            else if (_selectedSourceBaseId is int sourceId)
            {
                var point = ToNormalized(input.PointerPosition, viewport);
                var targetId = HitTester.FindBaseAt(point, _match.Bases);

                if (targetId is int target)
                {
                    var source = FindBase(sourceId);
                    if (target == sourceId)
                    {
                        // A press and release on the same base the human owns opens its action menu
                        // (phase 2's silent cancel on this gesture is gone) - `source` is guaranteed
                        // human-owned already, since only such a base is ever selected on press.
                        if (source is not null)
                        {
                            _openMenu = new BaseActionMenu(_match, _match.HumanPlayer, sourceId);
                        }
                    }
                    else if (source is not null && source.Owner == _match.HumanPlayer)
                    {
                        var unitCount = Math.Max(1, source.GarrisonCount / 2);
                        if (unitCount <= source.GarrisonCount)
                        {
                            _runner.Execute(new SendArmyCommand(_match.HumanPlayer, sourceId, target, unitCount));
                        }
                    }
                }
            }

            _selectedSourceBaseId = null;
        }

        _wasPointerPressed = input.IsPointerPressed;
    }

    /// <summary>
    /// While a menu is open, a press (the down) on one of its buttons is remembered so the matching
    /// release can activate it; a press anywhere else dismisses the menu immediately and swallows
    /// its own release - no selection is made even if the press lands on another owned base (D-26).
    /// </summary>
    private void HandlePressWhileMenuOpen(IInputSource input, Viewport viewport)
    {
        var point = ToNormalized(input.PointerPosition, viewport);
        var buttonIndex = _openMenu!.HitTestButton(point, viewport); // guarded by the caller's `_openMenu is not null` check

        if (buttonIndex is int index)
        {
            // Read from Core's own answer, cached on the menu since its last refresh - not computed
            // here (D-25). Whether this gesture can activate is decided once, at press time; the
            // release always honours it even if the answer changes underneath a held press.
            if (_openMenu.Actions[index].Availability == BaseActionAvailability.Affordable)
            {
                _pressBeganOnMenuButtonIndex = index;
            }
            else
            {
                _pressBeganOnGreyedMenuButton = true;
            }
        }
        else
        {
            _openMenu = null;
            _pressDismissedMenuOnThisPress = true;
        }
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

            // The level ring is drawn first, as a larger copy of the same circle in a darker shade of
            // the owner's tint, so the fill drawn on top of it leaves exactly a ring of that shade
            // visible around the rim - three thicknesses distinguishable at both target resolutions,
            // with no per-level literal here (the fraction lives in LevelTable, D-22).
            var ringThickness = radius * (float)b.RingThicknessFractionOfRadius;
            var ringRadius = radius + ringThickness;
            var ringDiameter = (int)(ringRadius * 2);
            var ringDestination = new Rectangle(
                (int)(center.X - ringRadius), (int)(center.Y - ringRadius), ringDiameter, ringDiameter);
            spriteBatch.Draw(_circleTexture, ringDestination, DarkenOwnerColor(GetOwnerColor(b.Owner)));

            // A base under construction (D-30, FR-3c) draws one further ring, outside the level ring,
            // in a fixed colour - distinguishable from both its current level (the darker ring just
            // drawn) and its completed target level (which is not drawn until completion at all).
            if (b.Construction is not null)
            {
                var constructionRingThickness = radius * _constructionRingThicknessFraction;
                var constructionRingRadius = ringRadius + constructionRingThickness;
                var constructionRingDiameter = (int)(constructionRingRadius * 2);
                var constructionRingDestination = new Rectangle(
                    (int)(center.X - constructionRingRadius),
                    (int)(center.Y - constructionRingRadius),
                    constructionRingDiameter,
                    constructionRingDiameter);
                spriteBatch.Draw(_circleTexture, constructionRingDestination, _constructionRingColor);
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

        if (_openMenu is not null && _buttonTexture is not null)
        {
            _openMenu.Draw(spriteBatch, _buttonTexture, _font, viewport);
        }

        if (_match.Outcome != MatchOutcome.InProgress)
        {
            DrawOutcomeBanner(spriteBatch, viewport);
        }
    }

    private static Color DarkenOwnerColor(Color color) => Color.Lerp(color, _ringDarkenTarget, _ringDarkenAmount);

    /// <summary>
    /// Victory/defeat text over the final board, sized and positioned from the viewport (D-14) - a
    /// small band near the top clears every base's circle and garrison count at both 1280x720 and
    /// 1920x1200, since the nearest base row sits no higher than y=0.25 normalized (FR-7).
    /// </summary>
    private void DrawOutcomeBanner(SpriteBatch spriteBatch, Viewport viewport)
    {
        if (_font is null)
        {
            return;
        }

        var text = _match.Outcome == MatchOutcome.HumanVictory ? "Victory" : "Defeat";
        var unscaledSize = _font.MeasureString(text);
        var textScale = (viewport.Height * 0.06f) / unscaledSize.Y;
        var textSize = unscaledSize * textScale;
        var textPosition = new Vector2((viewport.Width - textSize.X) / 2f, viewport.Height * 0.02f);
        spriteBatch.DrawString(_font, text, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
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
            var fraction = span > 0 ? (double)(_match.ElapsedTicks - army.LaunchTick) / span : 1.0;
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            var x = source.Position.X + ((target.Position.X - source.Position.X) * fraction);
            var y = source.Position.Y + ((target.Position.Y - source.Position.Y) * fraction);
            var center = new Vector2((float)(x * viewport.Width), (float)(y * viewport.Height));

            var destination = new Rectangle((int)(center.X - armyRadius), (int)(center.Y - armyRadius), armyDiameter, armyDiameter);
            spriteBatch.Draw(_circleTexture, destination, GetOwnerColor(army.Owner));

            if (!_armyUnitText.TryGetValue(army.Id, out var cached) || cached.Count != army.UnitCount)
            {
                cached = (army.UnitCount.ToString(CultureInfo.InvariantCulture), army.UnitCount);
                _armyUnitText[army.Id] = cached;
            }

            var text = cached.Text;

            var unscaledSize = _font.MeasureString(text);
            var textScale = (armyDiameter * 0.5f) / unscaledSize.Y;
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(center.X - (textSize.X / 2f), center.Y - (textSize.Y / 2f));
            spriteBatch.DrawString(_font, text, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// Writes the match's elapsed ticks, one line per base (id, owner, garrison, level, cap,
    /// building), one line per in-flight army, and one menu line to <paramref name="path"/>, for
    /// `--dump-state` to give QA exact numbers instead of pixels. The menu line is written by the
    /// screen, never by <see cref="MW3.Core.Match"/> - menu state is presentation state (D-26).
    /// </summary>
    internal void WriteStateDump(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(FormattableString.Invariant($"ElapsedTicks: {_match.ElapsedTicks}"));
        writer.WriteLine(FormattableString.Invariant($"Outcome: {_match.Outcome}"));

        foreach (var b in _match.Bases)
        {
            var owner = b.Owner?.ControllerKind.ToString() ?? "Neutral";
            var cap = b.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
            writer.WriteLine(FormattableString.Invariant(
                $"Base {b.Id}: Owner={owner} Garrison={b.GarrisonCount} Level={b.Level} Cap={cap} {FormatBuildingField(b)}"));
        }

        foreach (var army in _match.ArmiesInFlight)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"Army {army.Id}: Owner={army.Owner.ControllerKind} Source={army.SourceBaseId} Target={army.TargetBaseId} Count={army.UnitCount} Launch={army.LaunchTick} Arrival={army.ArrivalTick}"));
        }

        writer.WriteLine(FormatMenuDumpLine());
    }

    /// <summary>
    /// The `Building=` token: `none`, or the kind and target and completion tick of a base's pending
    /// construction (D-30, FR-3c) - `UpgradeToLevel3@1240` or `ConvertToTower@1300`.
    /// </summary>
    private static string FormatBuildingField(Base b) => b.Construction switch
    {
        null => "Building=none",
        PendingUpgrade upgrade => FormattableString.Invariant($"Building=UpgradeToLevel{upgrade.TargetLevel}@{upgrade.CompletionTick}"),
        PendingConversion conversion => FormattableString.Invariant(
            $"Building=Convert{(conversion.TargetType == BaseType.Tower ? "ToTower" : "ToProducer")}@{conversion.CompletionTick}"),
        _ => "Building=none",
    };

    private string FormatMenuDumpLine()
    {
        if (_openMenu is null)
        {
            return "Menu: none";
        }

        var anchorBase = FindBase(_openMenu.BaseId);
        var action = _openMenu.Actions.Count > 0 ? _openMenu.Actions[0] : null;
        if (anchorBase is null || action is null)
        {
            return "Menu: none";
        }

        var cap = anchorBase.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
        return FormattableString.Invariant(
            $"Menu: Base={anchorBase.Id} Garrison={anchorBase.GarrisonCount}/{cap} Upgrade={action.Availability} Cost={action.Cost}");
    }

    public void Dispose()
    {
        _circleTexture?.Dispose();
        _buttonTexture?.Dispose();
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

    private static Texture2D CreateButtonTexture(GraphicsDevice graphicsDevice)
    {
        var texture = new Texture2D(graphicsDevice, 1, 1);
        texture.SetData(new[] { Color.White });
        return texture;
    }
}
