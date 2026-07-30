namespace MW3.Core.Tests;

public class SendStrengthCalculatorTests
{
    [Theory]
    [InlineData(1, SendStrength.Quarter, 1)] // floor(0.25) = 0, clamped to 1
    [InlineData(2, SendStrength.Quarter, 1)] // floor(0.5) = 0, clamped to 1
    [InlineData(3, SendStrength.Quarter, 1)] // floor(0.75) = 0, clamped to 1
    [InlineData(4, SendStrength.Quarter, 1)]
    [InlineData(8, SendStrength.Quarter, 2)]
    [InlineData(20, SendStrength.Quarter, 5)]
    [InlineData(1, SendStrength.Half, 1)] // floor(0.5) = 0, clamped to 1
    [InlineData(2, SendStrength.Half, 1)]
    [InlineData(3, SendStrength.Half, 1)]
    [InlineData(8, SendStrength.Half, 4)]
    [InlineData(20, SendStrength.Half, 10)]
    [InlineData(1, SendStrength.ThreeQuarters, 1)] // floor(0.75) = 0, clamped to 1
    [InlineData(2, SendStrength.ThreeQuarters, 1)] // floor(1.5) = 1
    [InlineData(4, SendStrength.ThreeQuarters, 3)]
    [InlineData(8, SendStrength.ThreeQuarters, 6)]
    [InlineData(20, SendStrength.ThreeQuarters, 15)]
    [InlineData(1, SendStrength.Full, 1)]
    [InlineData(2, SendStrength.Full, 2)]
    [InlineData(8, SendStrength.Full, 8)]
    [InlineData(20, SendStrength.Full, 20)]
    public void Compute_FloorsAndClampsToOne(int garrison, SendStrength strength, int expected)
    {
        Assert.Equal(expected, SendStrengthCalculator.Compute(garrison, strength));
    }

    [Fact]
    public void SendStrength_HasExactlyFourMembers_WithMw2PercentageValues()
    {
        Assert.Equal(25, (int)SendStrength.Quarter);
        Assert.Equal(50, (int)SendStrength.Half);
        Assert.Equal(75, (int)SendStrength.ThreeQuarters);
        Assert.Equal(100, (int)SendStrength.Full);
        Assert.Equal(4, Enum.GetValues<SendStrength>().Length);
    }
}
