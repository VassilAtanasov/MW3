namespace MW3.Core.Tests;

public class SendWaveCalculatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(80)]
    [InlineData(100)]
    public void WaveCount_ReturnsCorrectNumberOfWaves(int unitCount)
    {
        var waveCount = SendWaveCalculator.WaveCount(unitCount);
        var expected = (unitCount + SendWaveCalculator.WaveSizeUnits - 1) / SendWaveCalculator.WaveSizeUnits;
        Assert.Equal(expected, waveCount);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(7, 1, 7)]
    [InlineData(8, 1, 8)]
    [InlineData(9, 1, 8)]
    [InlineData(9, 2, 1)]
    [InlineData(16, 1, 8)]
    [InlineData(16, 2, 8)]
    [InlineData(20, 1, 8)]
    [InlineData(20, 2, 8)]
    [InlineData(20, 3, 4)]
    [InlineData(80, 10, 8)]
    [InlineData(100, 13, 4)]  // Last wave in 13-wave send
    public void UnitsInWave_ReturnsCorrectUnitCount(int totalUnits, int waveIndex, int expected)
    {
        var actual = SendWaveCalculator.UnitsInWave(totalUnits, waveIndex);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(80)]
    [InlineData(100)]
    public void PerWaveUnits_SumToTotalUnits(int unitCount)
    {
        var waveCount = SendWaveCalculator.WaveCount(unitCount);
        var sum = 0;
        for (var waveIndex = 1; waveIndex <= waveCount; waveIndex++)
        {
            sum += SendWaveCalculator.UnitsInWave(unitCount, waveIndex);
        }

        Assert.Equal(unitCount, sum);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 15)]
    public void LaunchTickOffset_ReturnsCorrectOffset(int waveIndex, int expected)
    {
        var actual = SendWaveCalculator.LaunchTickOffset(waveIndex);
        Assert.Equal(expected, actual);
    }
}
