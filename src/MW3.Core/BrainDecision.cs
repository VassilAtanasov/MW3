namespace MW3.Core;

/// <summary>
/// A single <see cref="IPlayerBrain"/> decision: either no command this decision, or exactly one
/// of <see cref="SendArmyCommand"/>, <see cref="UpgradeCommand"/>, or <see cref="ConvertCommand"/> -
/// distinguished in the type system via an internal discriminant rather than by returning null, a
/// sentinel command, a list, or a bare bool (D-16, D-31, FR-7).
/// </summary>
public readonly record struct BrainDecision
{
    private enum Kind
    {
        None,
        Send,
        Upgrade,
        Convert,
    }

    private readonly Kind _kind;
    private readonly SendArmyCommand? _sendCommand;
    private readonly UpgradeCommand? _upgradeCommand;
    private readonly ConvertCommand? _convertCommand;

    private BrainDecision(Kind kind, SendArmyCommand? sendCommand, UpgradeCommand? upgradeCommand, ConvertCommand? convertCommand)
    {
        _kind = kind;
        _sendCommand = sendCommand;
        _upgradeCommand = upgradeCommand;
        _convertCommand = convertCommand;
    }

    public static readonly BrainDecision None = new(Kind.None, null, null, null);

    public static BrainDecision Send(SendArmyCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(Kind.Send, command, null, null);
    }

    public static BrainDecision Upgrading(UpgradeCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(Kind.Upgrade, null, command, null);
    }

    public static BrainDecision Converting(ConvertCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(Kind.Convert, null, null, command);
    }

    /// <summary>Whether this decision carries a send, upgrade, or convert command.</summary>
    public bool HasCommand => _kind != Kind.None;

    /// <summary>Whether the carried command is an <see cref="UpgradeCommand"/> rather than a send or convert.</summary>
    public bool IsUpgrade => _kind == Kind.Upgrade;

    /// <summary>Whether the carried command is a <see cref="ConvertCommand"/> rather than a send or upgrade.</summary>
    public bool IsConvert => _kind == Kind.Convert;

    public SendArmyCommand Command =>
        _kind == Kind.Send
            ? _sendCommand!
            : throw new InvalidOperationException("This decision carries no send command; check HasCommand, IsUpgrade, and IsConvert first.");

    public UpgradeCommand Upgrade =>
        _kind == Kind.Upgrade
            ? _upgradeCommand!
            : throw new InvalidOperationException("This decision carries no upgrade command; check HasCommand, IsUpgrade, and IsConvert first.");

    public ConvertCommand Convert =>
        _kind == Kind.Convert
            ? _convertCommand!
            : throw new InvalidOperationException("This decision carries no convert command; check HasCommand, IsUpgrade, and IsConvert first.");
}
