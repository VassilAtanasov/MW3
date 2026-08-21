using MW3.Core;

namespace MW3.Server;

/// <summary>
/// Turns a wire-shaped <see cref="GatewayCommand"/> into the matching <c>MW3.Core</c> command and
/// applies it against a <see cref="Match"/> on behalf of an explicit <see cref="Player"/>. Extracted
/// from <see cref="MatchSession"/>'s own client-command path (FR-6, D-89) so there is exactly one
/// translation from the wire shape to the rules' own commands, shared by the connected client's path
/// (always <see cref="Match.HumanPlayer"/>) and the replay-equivalence test's reader (whichever
/// player a logged command names) - mirrors <c>LoopbackMatchGateway.Submit</c> the same way the
/// original single-caller version did (D-66).
/// </summary>
internal static class GatewayCommandApplier
{
    /// <summary>The result of applying a command, plus - for a <see cref="GatewayCommandKind.SendArmy"/> - the exact unit count it actually committed.</summary>
    internal readonly record struct ApplyResult(GatewayCommandResult Result, int? SendUnitCount);

    /// <param name="match">The match to apply the command against.</param>
    /// <param name="player">Who is issuing it.</param>
    /// <param name="command">The command itself.</param>
    /// <param name="exactSendUnitCount">
    /// For a <see cref="GatewayCommandKind.SendArmy"/> only: the exact unit count to send, bypassing
    /// <see cref="SendStrengthCalculator"/>. FR-6's replay reader passes this - re-deriving the count
    /// from <paramref name="command"/>'s <see cref="GatewayCommand.Strength"/> against the replay's
    /// own garrison would recompute it from whatever the replay's garrison happens to be at that instant,
    /// which two otherwise-identical matches are not guaranteed to agree on to the exact unit at every
    /// tick along the way - only the tick at which a command actually applies is proven identical
    /// (D-89's hash checks). Null for the connected client's path, which has no logged count to trust
    /// and must compute one fresh, same as always.
    /// </param>
    internal static ApplyResult Apply(Match match, Player player, GatewayCommand command, int? exactSendUnitCount = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(command);

        switch (command.Kind)
        {
            case GatewayCommandKind.SendArmy:
                var source = FindBase(match, command.FromBaseId);
                if (source is null)
                {
                    return new ApplyResult(GatewayCommandResult.Rejected(SendArmyOutcome.BaseNotFound.ToString()), null);
                }

                var unitCount = exactSendUnitCount ?? SendStrengthCalculator.Compute(source.GarrisonCount, command.Strength!.Value);
                var sendOutcome = match.Execute(new SendArmyCommand(player, command.FromBaseId, command.ToBaseId!.Value, unitCount));
                return new ApplyResult(Describe(sendOutcome, SendArmyOutcome.Accepted), unitCount);

            case GatewayCommandKind.Upgrade:
                return new ApplyResult(Describe(match.Execute(new UpgradeCommand(player, command.FromBaseId)), UpgradeOutcome.Accepted), null);

            case GatewayCommandKind.Convert:
                return new ApplyResult(
                    Describe(match.Execute(new ConvertCommand(player, command.FromBaseId, command.TargetType!.Value)), ConvertOutcome.Accepted),
                    null);

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown gateway command kind.");
        }
    }

    /// <summary>True if <paramref name="baseId"/> names a base in <paramref name="match"/>.</summary>
    internal static Base? FindBase(Match match, int baseId)
    {
        ArgumentNullException.ThrowIfNull(match);

        var bases = match.Bases;
        for (var i = 0; i < bases.Count; i++)
        {
            if (bases[i].Id == baseId)
            {
                return bases[i];
            }
        }

        return null;
    }

    private static GatewayCommandResult Describe<TOutcome>(TOutcome outcome, TOutcome accepted)
        where TOutcome : struct, Enum =>
        EqualityComparer<TOutcome>.Default.Equals(outcome, accepted)
            ? GatewayCommandResult.Ok()
            : GatewayCommandResult.Rejected(outcome.ToString()!);
}
