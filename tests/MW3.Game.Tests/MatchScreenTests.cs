using System.Reflection;

namespace MW3.Game.Tests;

/// <summary>
/// FR-5: guards against the army-marker-larger-than-base inversion #94 raised, by reading the
/// private radius constants via reflection - <see cref="MatchScreen"/> draws with a graphics
/// device and is not otherwise unit-testable.
/// </summary>
public class MatchScreenTests
{
    private static float GetPrivateConst(string name) =>
        (float)typeof(MatchScreen)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [Fact]
    public void LeadArmyRadius_IsStrictlyLessThanBaseRadius()
    {
        var radiusFraction = GetPrivateConst("_radiusFraction");
        var armyRadiusFractionOfBase = GetPrivateConst("_armyRadiusFractionOfBase");

        Assert.True(
            armyRadiusFractionOfBase * radiusFraction < radiusFraction,
            "a lead army marker must draw smaller than the base it launched from");
    }

    [Fact]
    public void TrailingArmyRadius_IsStrictlyLessThanLeadArmyRadius()
    {
        var armyRadiusFractionOfBase = GetPrivateConst("_armyRadiusFractionOfBase");
        var armyTrailingRadiusFractionOfBase = GetPrivateConst("_armyTrailingRadiusFractionOfBase");

        Assert.True(armyTrailingRadiusFractionOfBase < armyRadiusFractionOfBase);
    }
}
