using System.Reflection;

namespace MW3.Game.Tests;

/// <summary>
/// Guards presentation sizing invariants that need no graphics device.
/// </summary>
public class MatchScreenTests
{
    private static float GetPrivateConst(string name) =>
        (float)typeof(MatchScreen)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [Fact]
    public void UnitSpriteDiameter_IsStrictlyLessThanBaseRadius()
    {
        var radiusFraction = GetPrivateConst("_radiusFraction");
        var spriteDiameter = ArmyCrowdPresentation.SpriteDiameterFractionOfMinDimension;

        Assert.True(
            spriteDiameter < radiusFraction * 2f,
            "a single marching unit must draw smaller than the base it launched from");
    }

    [Fact]
    public void CrowdFootprint_ForMaxWave_IsStillSmallerThanBaseDisc()
    {
        var radiusFraction = GetPrivateConst("_radiusFraction");
        var baseDiameter = radiusFraction * 2f;
        var maxCrowdWidth = ArmyCrowdPresentation.FootprintArea(
            ArmyCrowdPresentation.MaxSpritesPerArmy,
            waveIndex: 1,
            waveCount: 1);

        Assert.True(
            MathF.Sqrt(maxCrowdWidth) < baseDiameter * 1.5f,
            "even a full wave's crowd should not dwarf its source base");
    }
}
