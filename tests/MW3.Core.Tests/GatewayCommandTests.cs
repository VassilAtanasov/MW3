using System.Text.Json;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-3, D-76: the command vocabulary a client submits through <see cref="IMatchGateway"/>.
/// It is JSON-shaped with no polymorphism, so one record covers three kinds and validates at
/// construction which of its optional fields each kind may carry - the alternative being a payload
/// that deserializes cleanly and fails at whichever field the dispatcher dereferenced first.
/// </summary>
public class GatewayCommandTests
{
    [Fact]
    public void SendArmy_CarriesItsTargetAndStrengthAndNoTargetType()
    {
        var command = GatewayCommand.SendArmy(from: 0, to: 3, SendStrength.Quarter);

        Assert.Equal(GatewayCommandKind.SendArmy, command.Kind);
        Assert.Equal(0, command.FromBaseId);
        Assert.Equal(3, command.ToBaseId);
        Assert.Equal(SendStrength.Quarter, command.Strength);
        Assert.Null(command.TargetType);
    }

    [Fact]
    public void Upgrade_CarriesNothingButItsBase()
    {
        var command = GatewayCommand.Upgrade(baseId: 2);

        Assert.Equal(GatewayCommandKind.Upgrade, command.Kind);
        Assert.Equal(2, command.FromBaseId);
        Assert.Null(command.ToBaseId);
        Assert.Null(command.Strength);
        Assert.Null(command.TargetType);
    }

    [Fact]
    public void Convert_CarriesItsTargetTypeAndNothingElse()
    {
        var command = GatewayCommand.Convert(baseId: 2, BaseType.Tower);

        Assert.Equal(GatewayCommandKind.Convert, command.Kind);
        Assert.Equal(BaseType.Tower, command.TargetType);
        Assert.Null(command.ToBaseId);
        Assert.Null(command.Strength);
    }

    /// <summary>
    /// Every combination the kinds forbid, each rejected at construction rather than on use. Written
    /// out one case per line rather than generated: what each kind may carry is the contract, and a
    /// generator that derived the cases from the same table the validation reads would prove nothing.
    /// </summary>
    [Fact]
    public void EveryFieldCombinationAKindForbids_ThrowsAtConstruction()
    {
        // SendArmy: needs a target and a strength, must not name a type, must not target itself.
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.SendArmy, 0, null, SendStrength.Half, null));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.SendArmy, 0, 3, null, null));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.SendArmy, 0, 3, SendStrength.Half, BaseType.Tower));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.SendArmy, 3, 3, SendStrength.Half, null));

        // Upgrade: nothing but the base.
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Upgrade, 0, 3, null, null));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Upgrade, 0, null, SendStrength.Half, null));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Upgrade, 0, null, null, BaseType.Tower));

        // Convert: the type is required, and nothing else may be set.
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Convert, 0, null, null, null));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Convert, 0, 3, null, BaseType.Tower));
        Assert.Throws<ArgumentException>(() => new GatewayCommand(GatewayCommandKind.Convert, 0, null, SendStrength.Half, BaseType.Tower));
    }

    /// <summary>
    /// D-76's other half, asserted the only way an absence can be: nothing on the type names a
    /// player. If a field is ever added, this fails and whoever added it has to argue for it.
    /// </summary>
    [Fact]
    public void NoMemberOfTheCommand_NamesAPlayer()
    {
        var names = typeof(GatewayCommand)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.Name!)
            .ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Player", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Owner", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(GatewayCommandKind.SendArmy)]
    [InlineData(GatewayCommandKind.Upgrade)]
    [InlineData(GatewayCommandKind.Convert)]
    public void ACommand_RoundTripsThroughTheSourceGeneratedContext(GatewayCommandKind kind)
    {
        var command = kind switch
        {
            GatewayCommandKind.SendArmy => GatewayCommand.SendArmy(1, 4, SendStrength.ThreeQuarters),
            GatewayCommandKind.Upgrade => GatewayCommand.Upgrade(1),
            _ => GatewayCommand.Convert(1, BaseType.Forge),
        };

        var json = JsonSerializer.Serialize(command, MatchSnapshotJsonContext.Default.GatewayCommand);
        var restored = JsonSerializer.Deserialize(json, MatchSnapshotJsonContext.Default.GatewayCommand);

        Assert.Equal(command, restored);

        // Enum members travel as names, for the same reason the snapshot's do: an ordinal would let
        // a reordered BaseType silently reinterpret a Tower as a Forge.
        Assert.DoesNotContain("\"Kind\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Ok_AndRejected_AreDistinguishable_AndARejectionCarriesItsReason()
    {
        Assert.True(GatewayCommandResult.Ok().Accepted);
        Assert.Null(GatewayCommandResult.Ok().RejectionReason);

        var rejected = GatewayCommandResult.Rejected("SourceNotOwnedByIssuer");
        Assert.False(rejected.Accepted);
        Assert.Equal("SourceNotOwnedByIssuer", rejected.RejectionReason);

        Assert.Throws<ArgumentException>(() => GatewayCommandResult.Rejected("  "));
    }
}
