namespace MW3.Core;

/// <summary>
/// A single <see cref="IPlayerBrain"/> decision: either no command this decision, or exactly one
/// of <see cref="SendArmyCommand"/> or <see cref="UpgradeCommand"/> - distinguished in the type
/// system via an internal discriminant rather than by returning null, a sentinel command, a list,
/// or a bare bool (D-16, D-31). The shape is deliberately left room to grow a third case (FR-7's
/// convert) without another rewrite of every call site.
/// </summary>
public readonly record struct BrainDecision
{
    private enum Kind
    {
        None,
        Send,
        Upgrade,
    }

    private readonly Kind _kind;
    private readonly SendArmyCommand? _sendCommand;
    private readonly UpgradeCommand? _upgradeCommand;

    private BrainDecision(Kind kind, SendArmyCommand? sendCommand, UpgradeCommand? upgradeCommand)
    {
        _kind = kind;
        _sendCommand = sendCommand;
        _upgradeCommand = upgradeCommand;
    }

    public static readonly BrainDecision None = new(Kind.None, null, null);

    public static BrainDecision Send(SendArmyCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(Kind.Send, command, null);
    }

    public static BrainDecision Upgrading(UpgradeCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(Kind.Upgrade, null, command);
    }

    /// <summary>Whether this decision carries either a send or an upgrade command.</summary>
    public bool HasCommand => _kind != Kind.None;

    /// <summary>Whether the carried command is an <see cref="UpgradeCommand"/> rather than a send.</summary>
    public bool IsUpgrade => _kind == Kind.Upgrade;

    public SendArmyCommand Command =>
        _kind == Kind.Send
            ? _sendCommand!
            : throw new InvalidOperationException("This decision carries no send command; check HasCommand and IsUpgrade first.");

    public UpgradeCommand Upgrade =>
        _kind == Kind.Upgrade
            ? _upgradeCommand!
            : throw new InvalidOperationException("This decision carries no upgrade command; check HasCommand and IsUpgrade first.");
}
