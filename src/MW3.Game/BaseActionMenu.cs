using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MW3.Game;

/// <summary>
/// Lays out, draws, and hit-tests the action menu anchored above one base - decides nothing itself
/// (D-25). Buttons are placed on an arc above the anchor, clamped to stay fully inside the viewport.
///
/// Phase 8 FR-3: the actions come from <see cref="BaseSnapshot.AvailableActions"/> rather than from
/// a live call into the rules, and activating a button submits a <see cref="GatewayCommand"/>. The
/// change-detection cache stays, but only as what it always really was - a guard against
/// re-formatting button labels every frame. It now compares the action list itself rather than the
/// four fields the answer was known to depend on, so the menu cannot be stale for a reason nobody
/// enumerated: what is drawn is what the current snapshot says, always.
/// </summary>
internal sealed class BaseActionMenu
{
    private const float _buttonWidthFraction = 0.24f;
    private const float _buttonHeightFraction = 0.075f;
    private const float _arcRadiusFraction = 0.24f;
    private const float _viewportMarginFraction = 0.015f;
    private const float _headerHeightFraction = 0.06f;

    // The angular step between adjacent buttons on the arc (90 degrees, straight up, is the centre).
    // At the previous fixed 50-degree *total* spread, two buttons of _buttonWidthFraction width sat
    // only ~146px apart at _arcRadiusFraction's radius - narrower than the ~173px button itself, so
    // they always overlapped regardless of viewport clamping. Widened to 70 degrees when Convert
    // joined Upgrade on the arc (FR-5) so two buttons' chord distance clears the button width with
    // room to spare, at every anchor position, not only near an edge. Kept as a fixed *step* rather
    // than a fixed total spread when the third button joined (D-48, phase 6 FR-1): a fixed total
    // spread divided across N-1 gaps shrinks the adjacent spacing as buttons are added, which is what
    // made three buttons overlap even though two never did - stepping by degree keeps every adjacent
    // pair exactly as far apart as the original two-button layout, regardless of button count.
    private const float _arcStepDegrees = 70f;

    private static readonly Color _affordableColor = Color.DarkGoldenrod;
    private static readonly Color _greyedColor = Color.DimGray;
    private static readonly Color _headerColor = Color.SlateGray;

    private readonly IMatchGateway _gateway;

    private MatchSnapshot _snapshot;
    private int _lastGarrisonCount = -1;
    private int? _lastGarrisonCap = -1;
    private IReadOnlyList<BaseActionSnapshot> _actions = Array.Empty<BaseActionSnapshot>();
    private string[] _labels = Array.Empty<string>();

    // "<garrison> / <cap>" - the only place the cap is legible to the player (the map draws the
    // bare garrison count alone). Formatted only on refresh, not per frame.
    private string _garrisonLabel = string.Empty;

    public BaseActionMenu(IMatchGateway gateway, int baseId)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        _gateway = gateway;
        _snapshot = gateway.CurrentSnapshot;
        BaseId = baseId;

        Refresh();
    }

    public int BaseId { get; }

    public int ButtonCount => _actions.Count;

    /// <summary>Exposed only for <c>--dump-state</c>, which is presentation state (D-26).</summary>
    public IReadOnlyList<BaseActionSnapshot> Actions => _actions;

    /// <summary>
    /// What the gateway made of the last command this menu submitted, or null if it has submitted
    /// none. Carried, not drawn - a rejection indicator would change every screenshot, and FR-4 owns
    /// making one visible.
    /// </summary>
    public GatewayCommandResult? LastCommandResult { get; private set; }

    /// <summary>
    /// Re-reads the current snapshot, and re-formats the labels only if what they render has
    /// actually changed. The comparison is against the action list itself plus the two values the
    /// header renders, so no change to the rules' answer can slip through a cache key that did not
    /// know to watch for it.
    /// </summary>
    public void Refresh()
    {
        _snapshot = _gateway.CurrentSnapshot;

        var b = FindAnchorBase();
        if (b is null)
        {
            return;
        }

        if (b.GarrisonCount == _lastGarrisonCount && b.GarrisonCap == _lastGarrisonCap
            && ActionsEqual(_actions, b.AvailableActions))
        {
            return;
        }

        _lastGarrisonCount = b.GarrisonCount;
        _lastGarrisonCap = b.GarrisonCap;
        var cap = b.GarrisonCap is int capValue ? capValue.ToString(CultureInfo.InvariantCulture) : "none";
        _garrisonLabel = FormattableString.Invariant($"{b.GarrisonCount} / {cap}");
        _actions = b.AvailableActions;

        if (_labels.Length != _actions.Count)
        {
            _labels = new string[_actions.Count];
        }

        for (var i = 0; i < _actions.Count; i++)
        {
            _labels[i] = FormatLabel(_actions[i]);
        }
    }

    /// <summary>
    /// The button index at <paramref name="normalizedPoint"/>, or null if it falls outside every
    /// button - used both to gate activation (the press must land on a button) and to dismiss the
    /// menu when a press falls elsewhere.
    /// </summary>
    public int? HitTestButton(MapPoint normalizedPoint, Viewport viewport)
    {
        var pixel = new Point((int)(normalizedPoint.X * viewport.Width), (int)(normalizedPoint.Y * viewport.Height));

        for (var i = 0; i < _actions.Count; i++)
        {
            if (GetButtonRect(i, viewport).Contains(pixel))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Submits the command for <paramref name="buttonIndex"/> unconditionally - the caller only
    /// calls this once, for a press that began on a button this menu showed as affordable, and
    /// dismisses the menu regardless of what comes back. For Convert, the target type is read from
    /// the action itself (<see cref="BaseActionSnapshot.ConvertTargetType"/>) - this widget never
    /// decides which type to convert to (D-25, FR-5). The far side's outcome is authoritative: even
    /// a rejection (the garrison fell between opening and release) leaves match state untouched on
    /// its own and simply closes the menu, the same as an acceptance would (phase 2 #24's finding,
    /// standing in `docs/CONVENTIONS.md`). This method does not itself compare a cost to a garrison -
    /// that would repeat D-25's mistake one call up. The command names no player: the gateway
    /// attributes it to its own session's local player (D-76).
    /// </summary>
    public void Activate(int buttonIndex)
    {
        if (buttonIndex < 0 || buttonIndex >= _actions.Count)
        {
            return;
        }

        var action = _actions[buttonIndex];
        if (action.Kind == BaseActionKind.Upgrade)
        {
            LastCommandResult = _gateway.Submit(GatewayCommand.Upgrade(BaseId));
        }
        else if (action.Kind == BaseActionKind.Convert && action.ConvertTargetType is BaseType targetType)
        {
            LastCommandResult = _gateway.Submit(GatewayCommand.Convert(BaseId, targetType));
        }
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D buttonTexture, SpriteFont font, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(buttonTexture);
        ArgumentNullException.ThrowIfNull(font);

        if (_actions.Count > 0)
        {
            DrawHeader(spriteBatch, buttonTexture, font, viewport);
        }

        for (var i = 0; i < _actions.Count; i++)
        {
            var rect = GetButtonRect(i, viewport);
            var action = _actions[i];
            var color = action.Availability == BaseActionAvailability.Affordable ? _affordableColor : _greyedColor;
            spriteBatch.Draw(buttonTexture, rect, color);

            var label = _labels[i];
            var unscaledSize = font.MeasureString(label);
            var textScale = Math.Min((rect.Width * 0.85f) / unscaledSize.X, (rect.Height * 0.6f) / unscaledSize.Y);
            var textSize = unscaledSize * textScale;
            var textPosition = new Vector2(
                rect.X + ((rect.Width - textSize.X) / 2f),
                rect.Y + ((rect.Height - textSize.Y) / 2f));
            spriteBatch.DrawString(font, label, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// The anchored base's garrison against its cap (`12 / 35`) - the only place that number is
    /// legible to the player (D-22 acceptance: the map itself draws the bare count alone). Sits just
    /// above the button arc, clamped into the viewport exactly as each button is.
    /// </summary>
    private void DrawHeader(SpriteBatch spriteBatch, Texture2D buttonTexture, SpriteFont font, Viewport viewport)
    {
        var rect = GetHeaderRect(viewport);
        spriteBatch.Draw(buttonTexture, rect, _headerColor);

        var unscaledSize = font.MeasureString(_garrisonLabel);
        var textScale = Math.Min((rect.Width * 0.85f) / unscaledSize.X, (rect.Height * 0.7f) / unscaledSize.Y);
        var textSize = unscaledSize * textScale;
        var textPosition = new Vector2(
            rect.X + ((rect.Width - textSize.X) / 2f),
            rect.Y + ((rect.Height - textSize.Y) / 2f));
        spriteBatch.DrawString(font, _garrisonLabel, textPosition, Color.White, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// The header's destination rectangle - as wide as the union of every button, sitting just above
    /// the topmost one, clamped into the viewport independently (a base near the top edge can still
    /// clamp its buttons downward far enough that the header would otherwise land above y=0).
    /// </summary>
    private Rectangle GetHeaderRect(Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var margin = (int)(minDimension * _viewportMarginFraction);
        var headerHeight = (int)(minDimension * _headerHeightFraction);

        var unionLeft = int.MaxValue;
        var unionRight = int.MinValue;
        var unionTop = int.MaxValue;
        for (var i = 0; i < _actions.Count; i++)
        {
            var buttonRect = GetButtonRect(i, viewport);
            unionLeft = Math.Min(unionLeft, buttonRect.Left);
            unionRight = Math.Max(unionRight, buttonRect.Right);
            unionTop = Math.Min(unionTop, buttonRect.Top);
        }

        var width = unionRight - unionLeft;
        var top = Math.Clamp(unionTop - headerHeight - margin, margin, viewport.Height - headerHeight - margin);
        var left = Math.Clamp(unionLeft, margin, viewport.Width - width - margin);

        return new Rectangle(left, top, width, headerHeight);
    }

    /// <summary>
    /// One button's raw (unclamped) centre on the arc above the anchor base, at
    /// <paramref name="index"/> of <see cref="_actions"/>'s current count.
    /// </summary>
    private Vector2 GetRawButtonCenter(int index, Viewport viewport)
    {
        var anchor = FindAnchorBase();
        var anchorPosition = anchor?.Position ?? new MapPoint(0.5, 0.5);

        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var arcRadius = minDimension * _arcRadiusFraction;

        var count = Math.Max(1, _actions.Count);
        var totalSpreadDegrees = _arcStepDegrees * (count - 1);
        var angleDegrees = count == 1 ? 90.0 : 90.0 - (totalSpreadDegrees / 2.0) + (_arcStepDegrees * index);
        var angleRadians = angleDegrees * Math.PI / 180.0;

        var anchorPixel = new Vector2((float)(anchorPosition.X * viewport.Width), (float)(anchorPosition.Y * viewport.Height));
        var centerX = anchorPixel.X + (float)(arcRadius * Math.Cos(angleRadians));
        var centerY = anchorPixel.Y - (float)(arcRadius * Math.Sin(angleRadians));

        return new Vector2(centerX, centerY);
    }

    /// <summary>
    /// The single shift applied to every button so the whole arc moves together - never clamped one
    /// button at a time. Independent per-button clamping let two buttons near the viewport's left or
    /// top edge (a base drawn there, e.g. the map's top base row at y=0.25) overlap each other once
    /// Convert joined Upgrade on the arc (FR-5): each button clamped fully inside the viewport on its
    /// own, but nothing kept their rectangles from clamping onto the same spot. Shifting the group as
    /// one preserves the buttons' relative spacing while still guaranteeing every one lands fully
    /// inside the viewport.
    /// </summary>
    private (float Dx, float Dy) GetGroupShift(Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var buttonWidth = minDimension * _buttonWidthFraction;
        var buttonHeight = minDimension * _buttonHeightFraction;
        var margin = minDimension * _viewportMarginFraction;
        var headerHeight = minDimension * _headerHeightFraction;
        var topInset = margin + headerHeight + margin;

        var unionLeft = float.MaxValue;
        var unionRight = float.MinValue;
        var unionTop = float.MaxValue;
        var unionBottom = float.MinValue;
        for (var i = 0; i < _actions.Count; i++)
        {
            var center = GetRawButtonCenter(i, viewport);
            unionLeft = Math.Min(unionLeft, center.X - (buttonWidth / 2f));
            unionRight = Math.Max(unionRight, center.X + (buttonWidth / 2f));
            unionTop = Math.Min(unionTop, center.Y - (buttonHeight / 2f));
            unionBottom = Math.Max(unionBottom, center.Y + (buttonHeight / 2f));
        }

        var dx = 0f;
        if (unionLeft < margin)
        {
            dx = margin - unionLeft;
        }
        else if (unionRight > viewport.Width - margin)
        {
            dx = (viewport.Width - margin) - unionRight;
        }

        var dy = 0f;
        if (unionTop < topInset)
        {
            dy = topInset - unionTop;
        }
        else if (unionBottom > viewport.Height - margin)
        {
            dy = (viewport.Height - margin) - unionBottom;
        }

        return (dx, dy);
    }

    /// <summary>
    /// One button's destination rectangle, laid out on an arc above the anchor base and shifted, as
    /// a group with every other button (<see cref="GetGroupShift"/>), so the whole menu stays fully
    /// inside the viewport without any two buttons overlapping - exercised in practice by the map's
    /// top base row at y=0.25, which would otherwise draw its menu partly off-screen.
    /// </summary>
    private Rectangle GetButtonRect(int index, Viewport viewport)
    {
        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var buttonWidth = (int)(minDimension * _buttonWidthFraction);
        var buttonHeight = (int)(minDimension * _buttonHeightFraction);

        var center = GetRawButtonCenter(index, viewport);
        var (dx, dy) = GetGroupShift(viewport);

        var left = (int)(center.X + dx - (buttonWidth / 2f));
        var top = (int)(center.Y + dy - (buttonHeight / 2f));

        return new Rectangle(left, top, buttonWidth, buttonHeight);
    }

    private BaseSnapshot? FindAnchorBase()
    {
        var bases = _snapshot.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == BaseId)
            {
                return bases[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Element-wise action comparison, indexed rather than LINQ: this runs once per frame while a
    /// menu is open and must allocate nothing (docs/CONVENTIONS.md). The protocol's own list helper
    /// is internal to that assembly, so the comparison is written out here rather than reached for.
    /// </summary>
    private static bool ActionsEqual(IReadOnlyList<BaseActionSnapshot> left, IReadOnlyList<BaseActionSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A button's text. A convert button reads its <b>target type</b> and cost - <c>Producer: 30</c>,
    /// <c>Tower: 30</c>, <c>Forge: 30</c> - rather than the bare <c>Convert: 30</c> every convert
    /// button carried before phase 6 FR-5. With three base types a menu shows two convert buttons at
    /// once (D-48), and two buttons a player cannot tell apart before pressing one is the defect this
    /// fixes. The type name comes from <see cref="BaseActionSnapshot.ConvertTargetType"/>, which the action
    /// already carries so this widget never decides a target itself (D-25).
    /// <para>
    /// Only the text changes: button order, geometry, and the availability colouring are untouched
    /// (D-48), so every committed menu QA script's tap coordinates and button indices still hold.
    /// The upgrade arm below is byte-identical to what it has always emitted.
    /// </para>
    /// </summary>
    private static string FormatLabel(BaseActionSnapshot action) => action.Kind switch
    {
        // ConvertTargetType is non-null for every Convert action by BaseAction's own contract; the
        // fallback keeps a malformed action rendering as *something* rather than throwing inside a
        // Draw, and no code path produces it.
        BaseActionKind.Convert => action.Availability switch
        {
            BaseActionAvailability.UnderConstruction =>
                FormattableString.Invariant($"{ConvertTargetName(action)}: Building"),
            _ => FormattableString.Invariant($"{ConvertTargetName(action)}: {action.Cost}"),
        },
        _ => action.Availability switch
        {
            BaseActionAvailability.AlreadyAtMaxLevel => "Upgrade: Max",
            BaseActionAvailability.UnderConstruction => "Upgrade: Building",
            _ => FormattableString.Invariant($"Upgrade: {action.Cost}"),
        },
    };

    private static string ConvertTargetName(BaseActionSnapshot action) =>
        action.ConvertTargetType is BaseType target ? target.ToString() : "Convert";
}
