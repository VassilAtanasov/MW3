using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MW3.Core;

namespace MW3.Game;

/// <summary>
/// Lays out, draws, and hit-tests the action menu anchored above one base - decides nothing itself
/// (D-25). Buttons are placed on an arc above the anchor, clamped to stay fully inside the viewport,
/// and re-queries <see cref="Match.AvailableActions"/> only when the anchored base's garrison or
/// level actually changes, so it allocates nothing per frame while idle.
/// </summary>
internal sealed class BaseActionMenu
{
    private const float _buttonWidthFraction = 0.24f;
    private const float _buttonHeightFraction = 0.075f;
    private const float _arcRadiusFraction = 0.24f;
    private const float _viewportMarginFraction = 0.015f;

    private static readonly Color _affordableColor = Color.DarkGoldenrod;
    private static readonly Color _greyedColor = Color.DimGray;

    private readonly Match _match;
    private readonly Player _owner;

    private int _lastGarrisonCount = -1;
    private int _lastLevel = -1;
    private IReadOnlyList<BaseAction> _actions = Array.Empty<BaseAction>();
    private string[] _labels = Array.Empty<string>();

    public BaseActionMenu(Match match, Player owner, int baseId)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(owner);

        _match = match;
        _owner = owner;
        BaseId = baseId;

        Refresh();
    }

    public int BaseId { get; }

    public int ButtonCount => _actions.Count;

    /// <summary>Exposed only for <c>--dump-state</c>, which is presentation state (D-26).</summary>
    public IReadOnlyList<BaseAction> Actions => _actions;

    /// <summary>Re-queries Core only if the anchored base's garrison or level has actually changed.</summary>
    public void Refresh()
    {
        var b = FindAnchorBase();
        if (b is null)
        {
            return;
        }

        if (b.GarrisonCount == _lastGarrisonCount && b.Level == _lastLevel)
        {
            return;
        }

        _lastGarrisonCount = b.GarrisonCount;
        _lastLevel = b.Level;
        _actions = _match.AvailableActions(_owner, BaseId);

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
    /// dismisses the menu regardless of what <see cref="Match.Execute(UpgradeCommand)"/> returns.
    /// Core's outcome is authoritative: even a rejection (the garrison fell between opening and
    /// release) leaves match state untouched on its own and simply closes the menu, the same as an
    /// acceptance would (phase 2 #24's finding, standing in `docs/CONVENTIONS.md`). This method does
    /// not itself compare a cost to a garrison - that would repeat D-25's mistake one call up.
    /// </summary>
    public void Activate(int buttonIndex, MatchRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        if (buttonIndex < 0 || buttonIndex >= _actions.Count)
        {
            return;
        }

        if (_actions[buttonIndex].Kind == BaseActionKind.Upgrade)
        {
            runner.Execute(new UpgradeCommand(_owner, BaseId));
        }
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D buttonTexture, SpriteFont font, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(buttonTexture);
        ArgumentNullException.ThrowIfNull(font);

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
    /// One button's destination rectangle, laid out on an arc above the anchor base and clamped so
    /// the whole menu stays fully inside the viewport - exercised in practice by the map's top base
    /// row at y=0.25, which would otherwise draw its menu partly off-screen.
    /// </summary>
    private Rectangle GetButtonRect(int index, Viewport viewport)
    {
        var anchor = FindAnchorBase();
        var anchorPosition = anchor?.Position ?? new MapPoint(0.5, 0.5);

        var minDimension = Math.Min(viewport.Width, viewport.Height);
        var buttonWidth = (int)(minDimension * _buttonWidthFraction);
        var buttonHeight = (int)(minDimension * _buttonHeightFraction);
        var arcRadius = minDimension * _arcRadiusFraction;
        var margin = minDimension * _viewportMarginFraction;

        var count = Math.Max(1, _actions.Count);
        var angleDegrees = count == 1 ? 90.0 : 90.0 - 25.0 + (50.0 * index / (count - 1));
        var angleRadians = angleDegrees * Math.PI / 180.0;

        var anchorPixel = new Vector2((float)(anchorPosition.X * viewport.Width), (float)(anchorPosition.Y * viewport.Height));
        var centerX = anchorPixel.X + (float)(arcRadius * Math.Cos(angleRadians));
        var centerY = anchorPixel.Y - (float)(arcRadius * Math.Sin(angleRadians));

        var left = (int)(centerX - (buttonWidth / 2f));
        var top = (int)(centerY - (buttonHeight / 2f));

        left = Math.Clamp(left, (int)margin, viewport.Width - buttonWidth - (int)margin);
        top = Math.Clamp(top, (int)margin, viewport.Height - buttonHeight - (int)margin);

        return new Rectangle(left, top, buttonWidth, buttonHeight);
    }

    private Base? FindAnchorBase()
    {
        var bases = _match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == BaseId)
            {
                return bases[i];
            }
        }

        return null;
    }

    private static string FormatLabel(BaseAction action) =>
        action.Availability == BaseActionAvailability.AlreadyAtMaxLevel
            ? "Upgrade: Max"
            : FormattableString.Invariant($"Upgrade: {action.Cost}");
}
