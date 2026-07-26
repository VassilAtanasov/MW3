using System.Reflection;

namespace MW3.Core.Tests;

public class PlayerTests
{
    [Fact]
    public void PublicSurface_ExposesOnlyIdAndControllerKind()
    {
        var propertyNames = typeof(Player)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { nameof(Player.ControllerKind), nameof(Player.Id) }, propertyNames);
    }

    [Fact]
    public void Constructor_SetsIdAndControllerKind()
    {
        var player = new Player(Id: 7, PlayerControllerKind.Ai);

        Assert.Equal(7, player.Id);
        Assert.Equal(PlayerControllerKind.Ai, player.ControllerKind);
    }
}
