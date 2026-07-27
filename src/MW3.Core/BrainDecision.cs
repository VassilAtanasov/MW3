namespace MW3.Core;

/// <summary>
/// A single <see cref="IPlayerBrain"/> decision: either no command this decision, or exactly one
/// <see cref="SendArmyCommand"/> - distinguished in the type system rather than by returning null,
/// a sentinel command, or a list (D-16).
/// </summary>
public readonly record struct BrainDecision
{
    private readonly SendArmyCommand? _command;

    private BrainDecision(SendArmyCommand? command) => _command = command;

    public static readonly BrainDecision None = new(null);

    public static BrainDecision Send(SendArmyCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new BrainDecision(command);
    }

    public bool HasCommand => _command is not null;

    public SendArmyCommand Command =>
        _command ?? throw new InvalidOperationException("This decision carries no command; check HasCommand first.");
}
