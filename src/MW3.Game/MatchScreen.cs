using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Draws the match live: a circle per producer base and a square per tower base (FR-5), tinted by
/// owner, with its rising garrison count, plus the drag interaction that sends armies. Owns a fresh
/// <see cref="Match"/> and a fresh
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

    // FR-4, D-36: the last wave of a multi-wave send draws at half _armyRadiusFraction. At
    // 1280x720 that puts a tail marker's diameter (2 * 0.04 * 720 = 57.6px) under the horizontal
    // wave spacing (0.05 map units * 1280px = 64px) - the worst overlap the kickoff measured no
    // longer overlaps at all on that axis, and is materially reduced (69% -> 37.5%) on the
    // vertical one. A single-wave send never reaches this - RadiusFraction returns
    // _armyRadiusFraction unchanged whenever WaveCount <= 1 (D-36's bit-identical requirement).
    private const float _armyTrailingRadiusFraction = 0.04f;

    // The spine (D-36) is a thin line in the owner's tint connecting in-flight waves of one send,
    // drawn beneath their markers - thin enough it never reads as a marker itself.
    private const float _spineThicknessFraction = 0.006f;

    // A tower flashes for a few ticks after it fires (Base.LastFireTick), and a hit army flashes
    // for a few ticks after its UnitCount is observed to drop (FR-4, D-36) - long enough to survive
    // several Draw calls at a 50ms tick (docs/army-sending/ARCHITECTURE.md D-17), short enough to
    // read as an event rather than a standing state. Presentation-only: D-22's tuning table governs
    // simulation numbers, not these (the same call FR-2's kickoff made for its own constants).
    private const int _towerFlashDurationTicks = 4;
    private const int _armyHitFlashDurationTicks = 4;
    private const float _flashBrightenAmount = 0.6f;

    private static readonly Color _selectionHighlightColor = Color.Gold;
    private static readonly Color _flashBrightenTarget = Color.White;

    private readonly Match _match = new();
    private readonly MatchRunner _runner;

    private FixedStepClock _clock = new(Match.TickDurationMilliseconds);

    private SpriteFont? _font;
    private Texture2D? _circleTexture;

    // Towers are squares, producers stay circles (FR-5) - one extra texture, stretched by Rectangle
    // sizing exactly like the circle already is, never allocated per frame.
    private Texture2D? _squareTexture;

    // A tower's range is drawn as an outline (an annulus: transparent center, opaque rim) stretched
    // by a non-uniform Rectangle into an ellipse - the same stretch trick the circle texture already
    // uses for the base fill and its rings, applied here to map Core's normalized-space circular
    // range onto the viewport's non-square pixel aspect (FR-5). Created once, disposed alongside the
    // other textures.
    private Texture2D? _rangeRingTexture;
    private const float _rangeRingInnerFraction = 0.94f;
    private static readonly Color _rangeRingBaseColor = Color.White;

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

    // FR-4, D-36: the UnitCount an army had the last time Update observed it, and the tick its
    // count was last seen to drop - populated only in Update (never Draw), so a hit flash is tied
    // to tick arithmetic rather than frame cadence, and two decrements in quick succession are not
    // collapsed into one. An army destroyed outright leaves ArmiesInFlight the same tick its count
    // reaches zero, so its final hit never flashes - the accepted, documented limitation D-36 names.
    private readonly Dictionary<int, int> _lastArmyUnitCount = new();
    private readonly Dictionary<int, long> _armyHitTick = new();

    // Reused scratch buffer for PruneResolvedArmyText, so pruning stale cache entries allocates
    // nothing beyond its own one-time growth.
    private readonly List<int> _armyIdsToPrune = new();

    // Reused scratch buffers for DrawArmiesInFlight's spine (FR-4, D-36) - rebuilt every Draw call
    // via WaveColumnPresentation.ComputeSpineSegments, never allocated per frame.
    private readonly List<Vector2> _armyCenterScratch = new();
    private readonly List<(int FromIndex, int ToIndex)> _spineSegmentScratch = new();

    private bool _wasPointerPressed;
    private int? _selectedSourceBaseId;
    private bool _pressBeganAfterOutcomeDecided;

    // The action menu is presentation state only - MW3.Core never learns it exists (D-26).
    private BaseActionMenu? _openMenu;
    private int? _pressBeganOnMenuButtonIndex;
    private bool _pressBeganOnGreyedMenuButton;
    private bool _pressDismissedMenuOnThisPress;

    // The send-strength control is presentation state only, exactly like the menu above (FR-2,
    // D-26) - MW3.Core never learns which strength is selected, only the resulting UnitCount.
    private readonly SendStrengthSelector _strengthSelector = new();
    private int? _pressBeganOnStrengthButtonIndex;

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
        _squareTexture = CreateSquareTexture(graphicsDevice, diameter: 128);
        _rangeRingTexture = CreateRingTexture(graphicsDevice, diameter: 128, innerFraction: _rangeRingInnerFraction);
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
                RecordArmyHits();
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
    /// drag source; releasing over a different base issues a <see cref="SendArmyCommand"/> for the
    /// currently-selected <see cref="SendStrength"/>'s share of its garrison, via
    /// <see cref="SendStrengthCalculator"/> (read at release, floored, clamped to at least 1);
    /// releasing anywhere else cancels. Selection always clears on release, so the next press starts
    /// fresh (FR-5, D-18). A press that lands on the strength control instead (FR-2) is hit-tested
    /// first and never selects a base or starts a drag. Once decided: no drag is possible, and a
    /// release whose press began after the decision pops back to the welcome screen - a release from
    /// a press that began before it does not, so a drag already underway when the match ended cannot
    /// skip the result the player never saw (FR-7).
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
            _pressBeganOnStrengthButtonIndex = null;

            if (!outcomeDecided)
            {
                if (_openMenu is not null)
                {
                    HandlePressWhileMenuOpen(input, viewport);
                }
                else
                {
                    var point = ToNormalized(input.PointerPosition, viewport);
                    var strengthButtonIndex = SendStrengthSelector.HitTestButton(point, viewport);
                    if (strengthButtonIndex is int strengthIndex)
                    {
                        _pressBeganOnStrengthButtonIndex = strengthIndex;
                    }
                    else
                    {
                        var pressedBaseId = HitTester.FindBaseAt(point, _match.Bases);
                        var pressedBase = pressedBaseId is int id ? FindBase(id) : null;
                        _selectedSourceBaseId = pressedBase is not null && pressedBase.Owner == _match.HumanPlayer ? pressedBase.Id : null;
                    }
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
            else if (_pressBeganOnStrengthButtonIndex is int strengthButtonIndex)
            {
                _strengthSelector.Activate(strengthButtonIndex);
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
                        var unitCount = SendStrengthCalculator.Compute(source.GarrisonCount, _strengthSelector.SelectedStrength);
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
    /// FR-4, D-36: records, for every army still in flight, the tick its <see cref="Army.UnitCount"/>
    /// was last observed to drop - called once per tick from <see cref="Update"/>, never from
    /// <see cref="Draw"/>, so a hit flash is tied to tick arithmetic rather than frame cadence.
    /// </summary>
    private void RecordArmyHits()
    {
        var armies = _match.ArmiesInFlight;
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            if (_lastArmyUnitCount.TryGetValue(army.Id, out var previousCount) && army.UnitCount < previousCount)
            {
                _armyHitTick[army.Id] = _match.ElapsedTicks;
            }

            _lastArmyUnitCount[army.Id] = army.UnitCount;
        }
    }

    /// <summary>
    /// Drops cached unit-count text, last-seen-count, and last-hit-tick for armies no longer in
    /// flight, so <see cref="_armyUnitText"/>, <see cref="_lastArmyUnitCount"/>, and
    /// <see cref="_armyHitTick"/> do not grow for the life of a match as armies resolve.
    /// </summary>
    private void PruneResolvedArmyText()
    {
        if (_armyUnitText.Count == 0 && _lastArmyUnitCount.Count == 0 && _armyHitTick.Count == 0)
        {
            return;
        }

        var armies = _match.ArmiesInFlight;
        foreach (var id in _armyUnitText.Keys)
        {
            AddIfNotStillInFlight(armies, id);
        }

        foreach (var id in _lastArmyUnitCount.Keys)
        {
            AddIfNotStillInFlight(armies, id);
        }

        if (_armyIdsToPrune.Count == 0)
        {
            return;
        }

        foreach (var id in _armyIdsToPrune)
        {
            _armyUnitText.Remove(id);
            _lastArmyUnitCount.Remove(id);
            _armyHitTick.Remove(id);
        }

        _armyIdsToPrune.Clear();
    }

    private void AddIfNotStillInFlight(IReadOnlyList<Army> armies, int id)
    {
        if (_armyIdsToPrune.Contains(id))
        {
            return;
        }

        for (var i = 0; i < armies.Count; i++)
        {
            if (armies[i].Id == id)
            {
                return;
            }
        }

        _armyIdsToPrune.Add(id);
    }

    private static MapPoint ToNormalized(Point pointerPosition, Viewport viewport) =>
        new((double)pointerPosition.X / viewport.Width, (double)pointerPosition.Y / viewport.Height);

    public void Draw(SpriteBatch spriteBatch, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_font is null || _circleTexture is null || _squareTexture is null || _rangeRingTexture is null
            || _garrisonText is null || _lastGarrisonCount is null)
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

            // Shape follows the base's current, committed type (b.Type) - never a pending
            // conversion's target type, which only takes effect at Advance's completion tick (D-30,
            // FR-3c). A base converting into a tower stays a circle with no range until then; one
            // converting back to a producer stays a square with its range drawn right up to then.
            var shapeTexture = b.Type == BaseType.Tower ? _squareTexture : _circleTexture;

            if (b.Id == _selectedSourceBaseId)
            {
                var highlightRadius = radius * _selectionHighlightScale;
                var highlightDiameter = (int)(highlightRadius * 2);
                var highlightDestination = new Rectangle(
                    (int)(center.X - highlightRadius), (int)(center.Y - highlightRadius), highlightDiameter, highlightDiameter);
                spriteBatch.Draw(shapeTexture, highlightDestination, _selectionHighlightColor);
            }

            // The level ring is drawn first, as a larger copy of the same shape in a darker shade of
            // the owner's tint, so the fill drawn on top of it leaves exactly a ring of that shade
            // visible around the rim - three thicknesses distinguishable at both target resolutions,
            // with no per-level literal here (the fraction lives in LevelTable, D-22).
            var ringThickness = radius * (float)b.RingThicknessFractionOfRadius;
            var ringRadius = radius + ringThickness;
            var ringDiameter = (int)(ringRadius * 2);
            var ringDestination = new Rectangle(
                (int)(center.X - ringRadius), (int)(center.Y - ringRadius), ringDiameter, ringDiameter);
            spriteBatch.Draw(shapeTexture, ringDestination, DarkenOwnerColor(GetOwnerColor(b.Owner)));

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
                spriteBatch.Draw(shapeTexture, constructionRingDestination, _constructionRingColor);
            }

            // Every tower's range is drawn always, both owners, as an outline in the owner's tint -
            // read from LevelTable, never a literal here (FR-5). Core's range is a Euclidean distance
            // in normalized MapPoint units where X and Y both span 0..1, so a circle of radius R maps
            // to an ellipse of half-width R*viewport.Width and half-height R*viewport.Height on
            // screen - the same stretch-a-circle-into-a-Rectangle trick the base shapes already use.
            if (b.Type == BaseType.Tower)
            {
                var rangeUnits = LevelTable.Tower.RangeUnits(b.Level);
                var rangeHalfWidth = (float)(rangeUnits * viewport.Width);
                var rangeHalfHeight = (float)(rangeUnits * viewport.Height);
                var rangeDestination = new Rectangle(
                    (int)(center.X - rangeHalfWidth),
                    (int)(center.Y - rangeHalfHeight),
                    (int)(rangeHalfWidth * 2),
                    (int)(rangeHalfHeight * 2));
                spriteBatch.Draw(_rangeRingTexture, rangeDestination, GetOwnerColor(b.Owner));
            }

            var destination = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), diameter, diameter);
            var baseFillColor = GetOwnerColor(b.Owner);
            if (b.Type == BaseType.Tower && WaveColumnPresentation.IsFlashing(_match.ElapsedTicks, b.LastFireTick, _towerFlashDurationTicks))
            {
                baseFillColor = BrightenColor(baseFillColor);
            }

            spriteBatch.Draw(shapeTexture, destination, baseFillColor);

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

        // FR-5: read straight from _match every Draw call, never cached at screen entry, so a
        // morale change (capture, kill, upgrade, decay) lands in the meter the same frame it lands
        // in Match.
        MoraleMeter.Draw(spriteBatch, _circleTexture, viewport, _match.HumanMorale.Level, GetOwnerColor(_match.HumanPlayer), isHuman: true);
        MoraleMeter.Draw(spriteBatch, _circleTexture, viewport, _match.AiMorale.Level, GetOwnerColor(_match.AiPlayer), isHuman: false);

        if (_buttonTexture is not null)
        {
            _strengthSelector.Draw(spriteBatch, _buttonTexture, _font, viewport);
        }

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

    private static Color BrightenColor(Color color) => Color.Lerp(color, _flashBrightenTarget, _flashBrightenAmount);

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
    /// source at launch and exactly on the target at arrival (FR-5). A multi-wave send (FR-4, D-36)
    /// additionally draws a spine beneath the markers connecting its in-flight waves, and each
    /// wave's own radius tapers toward the tail so consecutive waves overlap less; a single-wave
    /// send draws bit-identically to before this feature (WaveCount == 1: no taper, no spine).
    /// </summary>
    private void DrawArmiesInFlight(SpriteBatch spriteBatch, Viewport viewport)
    {
        if (_font is null || _circleTexture is null || _buttonTexture is null)
        {
            return;
        }

        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var armies = _match.ArmiesInFlight;

        _armyCenterScratch.Clear();
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var source = FindBase(army.SourceBaseId);
            var target = FindBase(army.TargetBaseId);
            if (source is null || target is null)
            {
                _armyCenterScratch.Add(Vector2.Zero);
                continue;
            }

            var span = army.ArrivalTick - army.LaunchTick;
            var fraction = span > 0 ? (double)(_match.ElapsedTicks - army.LaunchTick) / span : 1.0;
            fraction = Math.Clamp(fraction, 0.0, 1.0);

            var x = source.Position.X + ((target.Position.X - source.Position.X) * fraction);
            var y = source.Position.Y + ((target.Position.Y - source.Position.Y) * fraction);
            _armyCenterScratch.Add(new Vector2((float)(x * viewport.Width), (float)(y * viewport.Height)));
        }

        WaveColumnPresentation.ComputeSpineSegments(armies, _spineSegmentScratch);
        var spineThickness = Math.Max(1f, minDimension * _spineThicknessFraction);
        for (var i = 0; i < _spineSegmentScratch.Count; i++)
        {
            var (fromIndex, toIndex) = _spineSegmentScratch[i];
            DrawSpineSegment(
                spriteBatch, _armyCenterScratch[fromIndex], _armyCenterScratch[toIndex], spineThickness, GetOwnerColor(armies[fromIndex].Owner));
        }

        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var center = _armyCenterScratch[i];

            var radiusFraction = WaveColumnPresentation.RadiusFraction(
                army.WaveIndex, army.WaveCount, _armyRadiusFraction, _armyTrailingRadiusFraction);
            var armyRadius = minDimension * radiusFraction;
            var armyDiameter = (int)(armyRadius * 2);

            var color = GetOwnerColor(army.Owner);
            if (_armyHitTick.TryGetValue(army.Id, out var hitTick)
                && WaveColumnPresentation.IsFlashing(_match.ElapsedTicks, hitTick, _armyHitFlashDurationTicks))
            {
                color = BrightenColor(color);
            }

            var destination = new Rectangle((int)(center.X - armyRadius), (int)(center.Y - armyRadius), armyDiameter, armyDiameter);
            spriteBatch.Draw(_circleTexture, destination, color);

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
    /// Draws a thin line from <paramref name="from"/> to <paramref name="to"/> by stretching and
    /// rotating the shared 1x1 <see cref="_buttonTexture"/> - the same reused-texture trick the
    /// range ring and buttons already use, so the spine needs no texture of its own.
    /// </summary>
    private void DrawSpineSegment(SpriteBatch spriteBatch, Vector2 from, Vector2 to, float thickness, Color color)
    {
        if (_buttonTexture is null)
        {
            return;
        }

        var delta = to - from;
        var length = delta.Length();
        if (length <= 0f)
        {
            return;
        }

        var rotation = MathF.Atan2(delta.Y, delta.X);
        var scale = new Vector2(length, thickness);
        spriteBatch.Draw(_buttonTexture, from, null, color, rotation, Vector2.Zero, scale, SpriteEffects.None, 0f);
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
        writer.WriteLine(FormattableString.Invariant(
            $"Morale: Human={_match.HumanMorale.Points} HumanLevel={_match.HumanMorale.Level} HumanAtk={MoraleTable.AttackPercentage(_match.HumanMorale.Level)} HumanDef={MoraleTable.DefencePercentage(_match.HumanMorale.Level)} Ai={_match.AiMorale.Points} AiLevel={_match.AiMorale.Level} AiAtk={MoraleTable.AttackPercentage(_match.AiMorale.Level)} AiDef={MoraleTable.DefencePercentage(_match.AiMorale.Level)}"));

        foreach (var b in _match.Bases)
        {
            var owner = b.Owner?.ControllerKind.ToString() ?? "Neutral";
            var cap = b.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
            writer.WriteLine(FormattableString.Invariant(
                $"Base {b.Id}: Owner={owner} Garrison={b.GarrisonCount} Level={b.Level} Cap={cap} {FormatBuildingField(b)} Type={b.Type}"));
        }

        foreach (var army in _match.ArmiesInFlight)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"Army {army.Id}: Owner={army.Owner.ControllerKind} Source={army.SourceBaseId} Target={army.TargetBaseId} Count={army.UnitCount} Launch={army.LaunchTick} Arrival={army.ArrivalTick} Send={army.SendId} Wave={army.WaveIndex}/{army.WaveCount}"));
        }

        writer.WriteLine(FormatMenuDumpLine());
        writer.WriteLine(FormattableString.Invariant($"Strength: {(int)_strengthSelector.SelectedStrength}"));
    }

    /// <summary>
    /// The `Building=` token: `none`, or the kind and target and completion tick of a base's pending
    /// construction (D-30, FR-3c) - `UpgradeToLevel3@1240`, `ConvertToTower@1300`, or (phase 6 FR-1)
    /// `ConvertToForge@1300`. Renders the target type by name so a third convert destination costs
    /// this line nothing beyond adding the enum member.
    /// </summary>
    private static string FormatBuildingField(Base b) => b.Construction switch
    {
        null => "Building=none",
        PendingUpgrade upgrade => FormattableString.Invariant($"Building=UpgradeToLevel{upgrade.TargetLevel}@{upgrade.CompletionTick}"),
        PendingConversion conversion => FormattableString.Invariant(
            $"Building=ConvertTo{conversion.TargetType}@{conversion.CompletionTick}"),
        _ => "Building=none",
    };

    /// <summary>
    /// The `Menu:` line (D-48): `Upgrade=`/`Cost=` keep their names and meaning, and one
    /// `Convert:&lt;TargetType&gt;=&lt;Availability&gt;@&lt;cost&gt;` token is written per convert
    /// action, in <see cref="Match.AvailableActions"/> order - replacing the old single
    /// `Convert=/ConvertCost=/ConvertTo=` triple now that a base can have more than one convert
    /// destination.
    /// </summary>
    private string FormatMenuDumpLine()
    {
        if (_openMenu is null)
        {
            return "Menu: none";
        }

        var anchorBase = FindBase(_openMenu.BaseId);
        var upgrade = _openMenu.Actions.Count > 0 ? _openMenu.Actions[0] : null;
        if (anchorBase is null || upgrade is null)
        {
            return "Menu: none";
        }

        var cap = anchorBase.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
        var line = FormattableString.Invariant(
            $"Menu: Base={anchorBase.Id} Garrison={anchorBase.GarrisonCount}/{cap} Upgrade={upgrade.Availability} Cost={upgrade.Cost}");

        for (var i = 1; i < _openMenu.Actions.Count; i++)
        {
            var convert = _openMenu.Actions[i];
            line += FormattableString.Invariant($" Convert:{convert.ConvertTargetType}={convert.Availability}@{convert.Cost}");
        }

        return line;
    }

    public void Dispose()
    {
        _circleTexture?.Dispose();
        _squareTexture?.Dispose();
        _rangeRingTexture?.Dispose();
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

    private static Texture2D CreateSquareTexture(GraphicsDevice graphicsDevice, int diameter)
    {
        var texture = new Texture2D(graphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        for (var j = 0; j < data.Length; j++)
        {
            data[j] = Color.White;
        }

        texture.SetData(data);
        return texture;
    }

    /// <summary>
    /// An annulus: transparent everywhere except a ring within <paramref name="innerFraction"/> of
    /// the outer radius, opaque there - stretched non-uniformly at draw time into an ellipse to
    /// render a tower's range (FR-5).
    /// </summary>
    private static Texture2D CreateRingTexture(GraphicsDevice graphicsDevice, int diameter, float innerFraction)
    {
        var texture = new Texture2D(graphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        var radius = diameter / 2f;
        var innerRadius = radius * innerFraction;
        var center = new Vector2(radius, radius);

        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                data[(y * diameter) + x] = distance <= radius && distance >= innerRadius ? _rangeRingBaseColor : Color.Transparent;
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
