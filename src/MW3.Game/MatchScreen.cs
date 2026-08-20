using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// Draws the match live: a circle per producer base, a square per tower base, and (phase 6 FR-5) an
/// upward-pointing triangle per forge base, tinted by owner, with its rising garrison count, plus
/// the drag interaction that sends armies.
///
/// Phase 8 FR-3: it holds no match. It renders a <see cref="MatchSnapshot"/> read from an
/// <see cref="IMatchGateway"/> and submits every command back through the same gateway, so this
/// class contains no rule and cannot reach one - the missing <c>MW3.Core</c> reference is what
/// proves it (D-57). The gateway is owned: this screen disposes it, so a match popped and pushed
/// again gets a wholly independent one.
///
/// Two consequences worth naming. Tick pacing moved into the gateway, which owns the clock, so this
/// class hands over elapsed milliseconds and never counts ticks itself. And an army's position is
/// computed here from <see cref="ArmyPathMath"/> plus the snapshot's elapsed ticks (D-68) - the same
/// arithmetic the rules resolve tower range with, so the drawn marker still cannot disagree with
/// where the simulation says it is, without the wire ever carrying a position.
/// </summary>
internal sealed class MatchScreen : IScreen
{
    private const float _radiusFraction = 0.075f;

    // FR-5: expressed as multiples of _radiusFraction rather than independent viewport fractions,
    // so a future base-radius change can never again let an army marker draw larger than a base -
    // the defect #94 raised. They reproduce the pre-FR-5 ratios: 0.5333 * 0.075 = 0.04 and
    // 0.2667 * 0.075 = 0.02.
    private const float _armyRadiusFractionOfBase = 0.5333f;
    private const float _armyTrailingRadiusFractionOfBase = 0.2667f;
    private const float _selectionHighlightScale = 1.35f;

    // FR-4, D-36: the last wave of a multi-wave send draws at half _armyRadiusFractionOfBase. At
    // 1280x720 with the FR-5 base radius (0.075 * 720 = 54px), a tail marker's diameter
    // (2 * 0.02 * 720 = 28.8px) is well under the horizontal wave spacing (0.05 map units * 1280px
    // = 64px), and equally clear of it on the vertical axis. A single-wave send never reaches this
    // - RadiusFraction returns the lead fraction unchanged whenever WaveCount <= 1 (D-36's
    // bit-identical requirement).

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

    private readonly IMatchGateway _gateway;

    private SpriteFont? _font;
    private Texture2D? _circleTexture;

    // Towers are squares, producers stay circles (FR-5) - one extra texture, stretched by Rectangle
    // sizing exactly like the circle already is, never allocated per frame.
    private Texture2D? _squareTexture;

    // Forges are an upward-pointing triangle (phase 6 FR-5), the third and last shape - same
    // stretch-by-Rectangle treatment, same lifetime as the other two.
    private Texture2D? _triangleTexture;

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

    // The Forges: readout's text, formatted only when the underlying count actually changes (phase
    // 6 FR-5, docs/CONVENTIONS.md's no-per-frame-allocation rule) - -1 is not a reachable forge
    // count, so it guarantees the first Draw call formats even when the true count starts at 0.
    private int _lastHumanForgeCount = -1;
    private int _lastAiForgeCount = -1;
    private string _humanForgesText = string.Empty;
    private string _aiForgesText = string.Empty;

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
    private readonly List<MapPoint> _spinePointScratch = new();

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

    /// <summary>
    /// Draws and drives the match <paramref name="gateway"/> exposes (phase 8 FR-3). The gateway is
    /// created per match by the composition root's factory, and this screen takes ownership of it:
    /// disposing the screen disposes the gateway.
    /// </summary>
    public MatchScreen(IMatchGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        _gateway = gateway;
    }

    public Color BackgroundColor => Color.DarkSlateGray;

    /// <summary>
    /// What the gateway made of the last send this screen submitted, or null if it has submitted
    /// none. Carried, not drawn: a rejection indicator would change every screenshot, and under the
    /// loopback gateway a rejection is unreachable in normal play anyway. FR-4 owns making it
    /// visible, when latency makes one reachable.
    /// </summary>
    internal GatewayCommandResult? LastCommandResult { get; private set; }

    public void LoadContent(ContentManager content, GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        _font = content.Load<SpriteFont>("Fonts/OpenSans");
        _circleTexture = CreateCircleTexture(graphicsDevice, diameter: 128);
        _squareTexture = CreateSquareTexture(graphicsDevice, diameter: 128);
        _triangleTexture = CreateTriangleTexture(graphicsDevice, diameter: 128);
        _rangeRingTexture = CreateRingTexture(graphicsDevice, diameter: 128, innerFraction: _rangeRingInnerFraction);
        _buttonTexture = CreateButtonTexture(graphicsDevice);

        var baseCount = _gateway.CurrentSnapshot.Bases.Count;
        _garrisonText = new string[baseCount];
        _lastGarrisonCount = new int[baseCount];
        for (var i = 0; i < _lastGarrisonCount.Length; i++)
        {
            _lastGarrisonCount[i] = -1;
        }
    }

    public void Update(IInputSource input, Viewport viewport, IScreenNavigator navigator, long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(navigator);

        var snapshot = _gateway.CurrentSnapshot;
        if (snapshot.Outcome == MatchOutcome.InProgress)
        {
            // The gateway owns the clock now (D-74), so this hands over wall-clock time and reads
            // back what came of it. Elapsed ticks are the snapshot's own sequence number, so a frame
            // in which the match actually moved is exactly a frame in which that number changed -
            // which is what the tick-driven hit-flash bookkeeping below keys on, as it always did.
            var previousElapsedTicks = snapshot.ElapsedTicks;
            _gateway.Advance(elapsedMilliseconds);
            snapshot = _gateway.CurrentSnapshot;

            if (snapshot.ElapsedTicks != previousElapsedTicks)
            {
                RecordArmyHits(snapshot);
                PruneResolvedArmyText(snapshot);
            }
        }

        if (snapshot.Outcome != MatchOutcome.InProgress)
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
            var anchorBase = FindBase(snapshot, _openMenu.BaseId);
            if (anchorBase is null || anchorBase.OwnerPlayerId != snapshot.LocalPlayerId || snapshot.Outcome != MatchOutcome.InProgress)
            {
                _openMenu = null;
            }
            else
            {
                _openMenu.Refresh();
            }
        }

        HandleInput(snapshot, input, viewport, navigator);
    }

    /// <summary>
    /// While the match is in progress: a press starting on a base the human owns selects it as the
    /// drag source; releasing over a different base submits a send-army
    /// <see cref="GatewayCommand"/> carrying the currently-selected <see cref="SendStrength"/> and
    /// no unit count - resolving a strength to a count is a rule, and phase 8 FR-3 moved it to the
    /// far side of the seam (D-76), so no arithmetic on a garrison happens in this file any more.
    /// Releasing anywhere else cancels. Selection always clears on release, so the next press starts
    /// fresh (FR-5, D-18). A press that lands on the strength control instead (FR-2) is hit-tested
    /// first and never selects a base or starts a drag. Once decided: no drag is possible, and a
    /// release whose press began after the decision pops back to the welcome screen - a release from
    /// a press that began before it does not, so a drag already underway when the match ended cannot
    /// skip the result the player never saw (FR-7).
    /// </summary>
    private void HandleInput(MatchSnapshot snapshot, IInputSource input, Viewport viewport, IScreenNavigator navigator)
    {
        var outcomeDecided = snapshot.Outcome != MatchOutcome.InProgress;

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
                        var pressedBaseId = HitTester.FindBaseAt(point, snapshot.Bases);
                        var pressedBase = pressedBaseId is int id ? FindBase(snapshot, id) : null;
                        _selectedSourceBaseId = pressedBase is not null && pressedBase.OwnerPlayerId == snapshot.LocalPlayerId ? pressedBase.Id : null;
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
                _openMenu?.Activate(buttonIndex);
                _openMenu = null;
            }
            else if (_pressBeganOnStrengthButtonIndex is int strengthButtonIndex)
            {
                _strengthSelector.Activate(strengthButtonIndex);
            }
            else if (_selectedSourceBaseId is int sourceId)
            {
                var point = ToNormalized(input.PointerPosition, viewport);
                var targetId = HitTester.FindBaseAt(point, snapshot.Bases);

                if (targetId is int target)
                {
                    var source = FindBase(snapshot, sourceId);
                    if (target == sourceId)
                    {
                        // A press and release on the same base the human owns opens its action menu
                        // (phase 2's silent cancel on this gesture is gone) - `source` is guaranteed
                        // human-owned already, since only such a base is ever selected on press.
                        if (source is not null)
                        {
                            _openMenu = new BaseActionMenu(_gateway, sourceId);
                        }
                    }
                    else if (source is not null && source.OwnerPlayerId == snapshot.LocalPlayerId)
                    {
                        // The strength goes over as a strength: what share of which garrison it comes
                        // to is resolved on the far side, at the tick the command applies (D-76). The
                        // "don't send from an empty base" guard this line used to carry went with it -
                        // the rules already reject that send, and a rejected command changes nothing,
                        // draws nothing (that is FR-4's) and leaves the dump byte-identical.
                        LastCommandResult = _gateway.Submit(
                            GatewayCommand.SendArmy(sourceId, target, _strengthSelector.SelectedStrength));
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

    // Indexed rather than foreach: Bases is IReadOnlyList<BaseSnapshot>, and enumerating a List<T>
    // through that interface boxes its struct enumerator on every call - not acceptable in code
    // reached from Draw (docs/CONVENTIONS.md's no-per-frame-allocation rule).
    private static BaseSnapshot? FindBase(MatchSnapshot snapshot, int id)
    {
        var bases = snapshot.Bases;
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
    /// FR-4, D-36: records, for every army still in flight, the tick its
    /// <see cref="ArmySnapshot.UnitCount"/> was last observed to drop - called only from
    /// <see cref="Update"/>, on a frame in which the match actually advanced, never from
    /// <see cref="Draw"/>, so a hit flash is tied to tick arithmetic rather than frame cadence.
    /// </summary>
    private void RecordArmyHits(MatchSnapshot snapshot)
    {
        var armies = snapshot.Armies;
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            if (_lastArmyUnitCount.TryGetValue(army.Id, out var previousCount) && army.UnitCount < previousCount)
            {
                _armyHitTick[army.Id] = snapshot.ElapsedTicks;
            }

            _lastArmyUnitCount[army.Id] = army.UnitCount;
        }
    }

    /// <summary>
    /// Drops cached unit-count text, last-seen-count, and last-hit-tick for armies no longer in
    /// flight, so <see cref="_armyUnitText"/>, <see cref="_lastArmyUnitCount"/>, and
    /// <see cref="_armyHitTick"/> do not grow for the life of a match as armies resolve.
    /// </summary>
    private void PruneResolvedArmyText(MatchSnapshot snapshot)
    {
        if (_armyUnitText.Count == 0 && _lastArmyUnitCount.Count == 0 && _armyHitTick.Count == 0)
        {
            return;
        }

        var armies = snapshot.Armies;
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

    private void AddIfNotStillInFlight(IReadOnlyList<ArmySnapshot> armies, int id)
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

        if (_font is null || _circleTexture is null || _squareTexture is null || _triangleTexture is null
            || _rangeRingTexture is null || _garrisonText is null || _lastGarrisonCount is null)
        {
            return;
        }

        // Captured once for the whole frame: every value drawn below comes from this one snapshot,
        // so nothing on screen can be a mixture of two of them.
        var snapshot = _gateway.CurrentSnapshot;

        DrawObstacles(spriteBatch, snapshot, viewport);

        var radius = Math.Min(viewport.Width, viewport.Height) * _radiusFraction;
        var diameter = (int)(radius * 2);
        var bases = snapshot.Bases;

        for (var i = 0; i < bases.Count; i++)
        {
            var b = bases[i];
            var center = new Vector2((float)(b.Position.X * viewport.Width), (float)(b.Position.Y * viewport.Height));

            // Shape follows the base's current, committed type (b.Type) - never a pending
            // conversion's target type, which only takes effect at Advance's completion tick (D-30,
            // FR-3c). A base converting into a tower stays a circle with no range until then; one
            // converting into or out of a forge (phase 6 FR-1, FR-5) stays its old shape and shows
            // only the construction ring until the same tick.
            var shapeTexture = b.Type switch
            {
                BaseType.Tower => _squareTexture,
                BaseType.Forge => _triangleTexture,
                _ => _circleTexture,
            };

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
            spriteBatch.Draw(shapeTexture, ringDestination, DarkenOwnerColor(GetOwnerColor(snapshot, b.OwnerPlayerId)));

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
            // read from the snapshot's own RangeUnits (phase 8 FR-3) - the LevelTable lookup that
            // stood here was the last rule this file ran, and it is now answered on the far side of
            // the seam. A range is a Euclidean distance in normalized MapPoint units where X and Y
            // both span 0..1, so a circle of radius R maps to an ellipse of half-width
            // R*viewport.Width and half-height R*viewport.Height on screen - the same
            // stretch-a-circle-into-a-Rectangle trick the base shapes already use.
            if (b.RangeUnits is double rangeUnits)
            {
                var rangeHalfWidth = (float)(rangeUnits * viewport.Width);
                var rangeHalfHeight = (float)(rangeUnits * viewport.Height);
                var rangeDestination = new Rectangle(
                    (int)(center.X - rangeHalfWidth),
                    (int)(center.Y - rangeHalfHeight),
                    (int)(rangeHalfWidth * 2),
                    (int)(rangeHalfHeight * 2));
                spriteBatch.Draw(_rangeRingTexture, rangeDestination, GetOwnerColor(snapshot, b.OwnerPlayerId));
            }

            var destination = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), diameter, diameter);
            var baseFillColor = GetOwnerColor(snapshot, b.OwnerPlayerId);
            if (b.Type == BaseType.Tower && WaveColumnPresentation.IsFlashing(snapshot.ElapsedTicks, b.LastFireTick, _towerFlashDurationTicks))
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

            // A triangle inscribes less area than a circle or square at the same bounding box, so
            // the garrison digits are sized and centred against its own incircle - the true largest
            // circle that fits inside it - rather than the bounding box's geometric centre, which
            // would let them overhang the sloped sides (phase 6 FR-5). The circle and square glyphs
            // are unchanged: their incircle is the same circle textScale already targeted.
            var textCenter = center;
            var textDiameterTarget = diameter;
            if (b.Type == BaseType.Forge)
            {
                var apexY = center.Y - radius;
                textCenter = new Vector2(center.X, apexY + (TriangleGeometry.IncenterYFraction() * diameter));
                textDiameterTarget = (int)(TriangleGeometry.InradiusFraction() * diameter * 2f);
            }

            var textScale = (textDiameterTarget * 0.5f) / unscaledSize.Y;
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(textCenter.X - (textSize.X / 2f), textCenter.Y - (textSize.Y / 2f));
            spriteBatch.DrawString(_font, garrisonText, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }

        DrawArmiesInFlight(spriteBatch, snapshot, viewport);

        // FR-5: read straight from the current snapshot every Draw call, never cached at screen
        // entry, so a morale change (capture, kill, upgrade, decay) lands in the meter the same frame
        // it lands in the match.
        var human = FindPlayer(snapshot, PlayerControllerKind.Human);
        var ai = FindPlayer(snapshot, PlayerControllerKind.Ai);
        MoraleMeter.Draw(spriteBatch, _circleTexture, viewport, human.MoraleLevel, GetPlayerColor(human.ControllerKind), isHuman: true);
        MoraleMeter.Draw(spriteBatch, _circleTexture, viewport, ai.MoraleLevel, GetPlayerColor(ai.ControllerKind), isHuman: false);

        // Phase 6 FR-5: same read-fresh-every-Draw rule as the morale meters above, formatted only
        // on change (docs/CONVENTIONS.md). Always drawn, including "Forges: 0" - hiding it at zero
        // would be indistinguishable from the readout not existing.
        var humanForgeCount = human.ForgeCount;
        if (_lastHumanForgeCount != humanForgeCount)
        {
            _humanForgesText = FormattableString.Invariant($"Forges: {humanForgeCount}");
            _lastHumanForgeCount = humanForgeCount;
        }

        var aiForgeCount = ai.ForgeCount;
        if (_lastAiForgeCount != aiForgeCount)
        {
            _aiForgesText = FormattableString.Invariant($"Forges: {aiForgeCount}");
            _lastAiForgeCount = aiForgeCount;
        }

        ForgesReadout.Draw(spriteBatch, _font, viewport, _humanForgesText, GetPlayerColor(human.ControllerKind), isHuman: true);
        ForgesReadout.Draw(spriteBatch, _font, viewport, _aiForgesText, GetPlayerColor(ai.ControllerKind), isHuman: false);

        if (_buttonTexture is not null)
        {
            _strengthSelector.Draw(spriteBatch, _buttonTexture, _font, viewport);
        }

        if (_openMenu is not null && _buttonTexture is not null)
        {
            _openMenu.Draw(spriteBatch, _buttonTexture, _font, viewport);
        }

        if (snapshot.Outcome != MatchOutcome.InProgress)
        {
            DrawOutcomeBanner(spriteBatch, snapshot, viewport);
        }
    }

    private static Color DarkenOwnerColor(Color color) => Color.Lerp(color, _ringDarkenTarget, _ringDarkenAmount);

    private static Color BrightenColor(Color color) => Color.Lerp(color, _flashBrightenTarget, _flashBrightenAmount);

    /// <summary>
    /// Victory/defeat text over the final board, sized and positioned from the viewport (D-14) - a
    /// small band near the top clears every base's circle and garrison count at both 1280x720 and
    /// 1920x1200, since the nearest base row sits no higher than y=0.25 normalized (FR-7).
    /// </summary>
    private void DrawOutcomeBanner(SpriteBatch spriteBatch, MatchSnapshot snapshot, Viewport viewport)
    {
        if (_font is null)
        {
            return;
        }

        var text = snapshot.Outcome == MatchOutcome.HumanVictory ? "Victory" : "Defeat";
        var unscaledSize = _font.MeasureString(text);
        var textScale = (viewport.Height * 0.06f) / unscaledSize.Y;
        var textSize = unscaledSize * textScale;
        var textPosition = new Vector2((viewport.Width - textSize.X) / 2f, viewport.Height * 0.02f);
        spriteBatch.DrawString(_font, text, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// Every <see cref="MatchSnapshot.Obstacles"/> entry as a filled <see cref="Color.SaddleBrown"/>
    /// rectangle (FR-4), drawn first - before a base, army marker, spine, action menu, strength
    /// selector, morale meter or <c>Forges:</c> readout, so nothing already on screen is ever hidden
    /// under terrain. Reuses the shared 1x1 <see cref="_buttonTexture"/>, the same stretch-by-
    /// Rectangle trick the spine and range ring already use, so this needs no texture of its own. A
    /// map with no obstacles (Small, Big) issues no draw call here.
    /// </summary>
    private void DrawObstacles(SpriteBatch spriteBatch, MatchSnapshot snapshot, Viewport viewport)
    {
        if (_buttonTexture is null)
        {
            return;
        }

        var obstacles = snapshot.Obstacles;
        for (var i = 0; i < obstacles.Count; i++)
        {
            var obstacle = obstacles[i];
            var minX = (int)(obstacle.MinX * viewport.Width);
            var minY = (int)(obstacle.MinY * viewport.Height);
            var maxX = (int)(obstacle.MaxX * viewport.Width);
            var maxY = (int)(obstacle.MaxY * viewport.Height);
            var destination = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            spriteBatch.Draw(_buttonTexture, destination, Color.SaddleBrown);
        }
    }

    /// <summary>
    /// Each in-flight army is a filled circle smaller than a base, tinted by owner, positioned by
    /// <see cref="ArmyPathMath.PositionAt(IReadOnlyList{MapPoint}, double, long, long, long)"/> - the
    /// same polyline walk the rules themselves resolve tower range against for the same army and
    /// tick (D-68), so the drawn marker can never disagree with where the simulation says it is
    /// (FR-4), and the wire still carries no position. A multi-wave send (FR-4, D-36) additionally draws a spine
    /// beneath the markers connecting its in-flight waves along that same path, and each wave's own
    /// radius tapers toward the tail so consecutive waves overlap less; a single-wave send draws
    /// bit-identically to before this feature (WaveCount == 1: no taper, no spine).
    /// </summary>
    private void DrawArmiesInFlight(SpriteBatch spriteBatch, MatchSnapshot snapshot, Viewport viewport)
    {
        if (_font is null || _circleTexture is null || _buttonTexture is null)
        {
            return;
        }

        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var armies = snapshot.Armies;
        var elapsedTicks = snapshot.ElapsedTicks;

        _armyCenterScratch.Clear();
        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var position = ArmyPathMath.PositionAt(army.PathWaypoints, army.PathLength, army.LaunchTick, army.ArrivalTick, elapsedTicks);
            _armyCenterScratch.Add(new Vector2((float)(position.X * viewport.Width), (float)(position.Y * viewport.Height)));
        }

        WaveColumnPresentation.ComputeSpineSegments(armies, _spineSegmentScratch);
        var spineThickness = Math.Max(1f, minDimension * _spineThicknessFraction);
        for (var i = 0; i < _spineSegmentScratch.Count; i++)
        {
            var (fromIndex, toIndex) = _spineSegmentScratch[i];
            var fromArmy = armies[fromIndex];
            var toArmy = armies[toIndex];
            var color = GetOwnerColor(snapshot, fromArmy.OwnerPlayerId);

            WaveColumnPresentation.ComputeSpinePoints(
                fromArmy.PathWaypoints,
                fromArmy.PathLength,
                ArmyPathMath.ProgressAt(fromArmy.LaunchTick, fromArmy.ArrivalTick, elapsedTicks),
                ArmyPathMath.ProgressAt(toArmy.LaunchTick, toArmy.ArrivalTick, elapsedTicks),
                _spinePointScratch);

            var previous = _armyCenterScratch[fromIndex];
            var lastPointIndex = _spinePointScratch.Count - 1;
            for (var p = 1; p <= lastPointIndex; p++)
            {
                var current = p == lastPointIndex
                    ? _armyCenterScratch[toIndex]
                    : new Vector2((float)(_spinePointScratch[p].X * viewport.Width), (float)(_spinePointScratch[p].Y * viewport.Height));
                DrawSpineSegment(spriteBatch, previous, current, spineThickness, color);
                previous = current;
            }
        }

        for (var i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var center = _armyCenterScratch[i];

            var radiusFraction = WaveColumnPresentation.RadiusFraction(
                army.WaveIndex,
                army.WaveCount,
                _armyRadiusFractionOfBase * _radiusFraction,
                _armyTrailingRadiusFractionOfBase * _radiusFraction);
            var armyRadius = minDimension * radiusFraction;
            var armyDiameter = (int)(armyRadius * 2);

            var color = GetOwnerColor(snapshot, army.OwnerPlayerId);
            if (_armyHitTick.TryGetValue(army.Id, out var hitTick)
                && WaveColumnPresentation.IsFlashing(elapsedTicks, hitTick, _armyHitFlashDurationTicks))
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
    /// `--dump-state` to give QA exact numbers instead of pixels.
    ///
    /// Every match line is formatted from a <see cref="MatchSnapshot"/> rather than from
    /// a live match (D-69). That direction matters more than it looks: this method
    /// was already a snapshot serializer in a bespoke text format, so making it render from the real
    /// snapshot turns every committed <c>qa/scripts/</c> run into evidence that the snapshot is a
    /// complete and faithful view of a match - a far stronger standard than tests written by the
    /// session that decided what "complete" means. The output is required to stay byte-identical,
    /// which is the check that keeps the evidence honest.
    ///
    /// The `Menu:` and `Strength:` lines are the exception, and stay the screen's own: menu state and
    /// the selected send strength are presentation state (D-26), not part of the match, and the fact
    /// that exactly those two lines resist the move is itself evidence the snapshot's scope is right.
    /// </summary>
    internal void WriteStateDump(string path)
    {
        var snapshot = _gateway.CurrentSnapshot;

        using var writer = new StreamWriter(path);
        writer.WriteLine(FormattableString.Invariant($"ElapsedTicks: {snapshot.ElapsedTicks}"));
        writer.WriteLine(FormattableString.Invariant($"Outcome: {snapshot.Outcome}"));

        var human = FindPlayer(snapshot, PlayerControllerKind.Human);
        var ai = FindPlayer(snapshot, PlayerControllerKind.Ai);
        writer.WriteLine(FormattableString.Invariant(
            $"Morale: Human={human.MoralePoints} HumanLevel={human.MoraleLevel} HumanAtk={human.MoraleAttackPercentage} HumanDef={human.MoraleDefencePercentage} Ai={ai.MoralePoints} AiLevel={ai.MoraleLevel} AiAtk={ai.MoraleAttackPercentage} AiDef={ai.MoraleDefencePercentage}"));

        // Phase 6 FR-3: count plus the two resulting percentages, the shape MW2-RULES.md §2.4 uses
        // to express a forge holding. The percentages come from the snapshot, which read them from
        // ForgeTable - so this line can never quietly disagree with the indices combat actually
        // composes, and since FR-3 the client has no forge table to disagree with.
        writer.WriteLine(FormattableString.Invariant(
            $"Forges: Human={human.ForgeCount} HumanAtk={human.ForgeAttackPercentage} HumanDef={human.ForgeDefencePercentage} Ai={ai.ForgeCount} AiAtk={ai.ForgeAttackPercentage} AiDef={ai.ForgeDefencePercentage}"));

        foreach (var b in snapshot.Bases)
        {
            var owner = OwnerName(snapshot, b.OwnerPlayerId);
            var cap = b.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
            writer.WriteLine(FormattableString.Invariant(
                $"Base {b.Id}: Owner={owner} Garrison={b.GarrisonCount} Level={b.Level} Cap={cap} {FormatBuildingField(b)} Type={b.Type}"));
        }

        foreach (var army in snapshot.Armies)
        {
            writer.WriteLine(FormattableString.Invariant(
                $"Army {army.Id}: Owner={OwnerName(snapshot, army.OwnerPlayerId)} Source={army.SourceBaseId} Target={army.TargetBaseId} Count={army.UnitCount} Launch={army.LaunchTick} Arrival={army.ArrivalTick} Send={army.SendId} Wave={army.WaveIndex}/{army.WaveCount}"));
        }

        writer.WriteLine(FormatMenuDumpLine(snapshot));
        writer.WriteLine(FormattableString.Invariant($"Strength: {(int)_strengthSelector.SelectedStrength}"));
    }

    /// <summary>
    /// The player in <paramref name="snapshot"/> with <paramref name="kind"/>. The dump names the two
    /// players by controller kind (`Human=`, `Ai=`) where the snapshot names them by id, because the
    /// dump's format predates the snapshot and D-69 requires it byte-identical.
    /// </summary>
    private static PlayerSnapshot FindPlayer(MatchSnapshot snapshot, PlayerControllerKind kind)
    {
        foreach (var player in snapshot.Players)
        {
            if (player.ControllerKind == kind)
            {
                return player;
            }
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"The snapshot carries no {kind} player."));
    }

    /// <summary>
    /// How the dump names an owner: the controller kind of the player whose id it is, or `Neutral`
    /// for the absence of one (D-11).
    /// </summary>
    private static string OwnerName(MatchSnapshot snapshot, int? ownerPlayerId)
    {
        if (ownerPlayerId is not int id)
        {
            return "Neutral";
        }

        foreach (var player in snapshot.Players)
        {
            if (player.Id == id)
            {
                return player.ControllerKind.ToString();
            }
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"The snapshot carries no player with id {id}."));
    }

    /// <summary>
    /// The `Building=` token: `none`, or the kind and target and completion tick of a base's pending
    /// construction (D-30, FR-3c) - `UpgradeToLevel3@1240`, `ConvertToTower@1300`, or (phase 6 FR-1)
    /// `ConvertToForge@1300`. Renders the target type by name so a third convert destination costs
    /// this line nothing beyond adding the enum member.
    /// </summary>
    private static string FormatBuildingField(BaseSnapshot b) => b.Construction switch
    {
        null => "Building=none",
        { Kind: BaseActionKind.Upgrade, TargetLevel: int level } construction =>
            FormattableString.Invariant($"Building=UpgradeToLevel{level}@{construction.CompletionTick}"),
        { Kind: BaseActionKind.Convert, TargetType: BaseType type } construction =>
            FormattableString.Invariant($"Building=ConvertTo{type}@{construction.CompletionTick}"),
        _ => "Building=none",
    };

    /// <summary>
    /// The `Menu:` line (D-48): `Upgrade=`/`Cost=` keep their names and meaning, and one
    /// `Convert:&lt;TargetType&gt;=&lt;Availability&gt;@&lt;cost&gt;` token is written per convert
    /// action, in the rules' own available-actions order - replacing the old single
    /// `Convert=/ConvertCost=/ConvertTo=` triple now that a base can have more than one convert
    /// destination.
    /// </summary>
    private string FormatMenuDumpLine(MatchSnapshot snapshot)
    {
        if (_openMenu is null)
        {
            return "Menu: none";
        }

        var anchorBase = FindBase(snapshot, _openMenu.BaseId);
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
        // The gateway is this screen's: created for this match by the composition root's factory,
        // handed over at construction, and released here - so a match popped and pushed again
        // starts wholly fresh, with no state carried over (`play-then-back`, `back-and-forth`).
        _gateway.Dispose();

        _circleTexture?.Dispose();
        _squareTexture?.Dispose();
        _triangleTexture?.Dispose();
        _rangeRingTexture?.Dispose();
        _buttonTexture?.Dispose();
    }

    /// <summary>
    /// The tint for whoever owns something, or grey for the absence of an owner (D-11). Resolves an
    /// owner id against the snapshot's own player list rather than against a
    /// <c>Player</c> - two players, so a linear scan costs nothing and allocates nothing, which
    /// matters because this is called from inside the base and army draw loops.
    /// </summary>
    private static Color GetOwnerColor(MatchSnapshot snapshot, int? ownerPlayerId)
    {
        if (ownerPlayerId is not int id)
        {
            return Color.Gray;
        }

        var players = snapshot.Players;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].Id == id)
            {
                return GetPlayerColor(players[i].ControllerKind);
            }
        }

        return Color.Gray;
    }

    private static Color GetPlayerColor(PlayerControllerKind controllerKind) => controllerKind switch
    {
        PlayerControllerKind.Human => Color.RoyalBlue,
        PlayerControllerKind.Ai => Color.Firebrick,
        _ => Color.Gray,
    };

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
    /// An upward-pointing triangle - apex at the top-centre, base along the bottom edge - filling
    /// the same <paramref name="diameter"/> x <paramref name="diameter"/> square the circle and
    /// square textures fill, so a forge occupies the same footprint as any other base (phase 6
    /// FR-5). Rasterized with <see cref="TriangleGeometry.Contains"/>, the same pure geometry
    /// <see cref="Draw"/> uses to position the garrison digits inside it.
    /// </summary>
    private static Texture2D CreateTriangleTexture(GraphicsDevice graphicsDevice, int diameter)
    {
        var texture = new Texture2D(graphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];

        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                data[(y * diameter) + x] = TriangleGeometry.Contains(x, y, diameter) ? Color.White : Color.Transparent;
            }
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
