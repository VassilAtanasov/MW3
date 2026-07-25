namespace MW3.Core.Tests;

public class FixedStepClockTests
{
    [Fact]
    public void Advance_WholeMultipleOfTickDuration_ReturnsExactTickCount()
    {
        var clock = new FixedStepClock(tickDurationMilliseconds: 16);

        var (_, ticks) = clock.Advance(48);

        Assert.Equal(3, ticks);
    }

    [Fact]
    public void Advance_RemainderTime_IsCarriedToNextCall()
    {
        var clock = new FixedStepClock(tickDurationMilliseconds: 16);

        var (afterFirst, firstTicks) = clock.Advance(20);
        var (_, secondTicks) = afterFirst.Advance(12);

        Assert.Equal(1, firstTicks);
        Assert.Equal(4, afterFirst.CarryOverMilliseconds);
        Assert.Equal(1, secondTicks);
    }

    [Fact]
    public void Advance_ZeroElapsed_ProducesZeroTicks()
    {
        var clock = new FixedStepClock(tickDurationMilliseconds: 16);

        var (next, ticks) = clock.Advance(0);

        Assert.Equal(0, ticks);
        Assert.Equal(0, next.CarryOverMilliseconds);
    }

    [Fact]
    public void Advance_NegativeElapsed_Throws()
    {
        var clock = new FixedStepClock(tickDurationMilliseconds: 16);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
    }

    [Fact]
    public void Constructor_NonPositiveTickDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(tickDurationMilliseconds: 0));
    }

    [Fact]
    public void Advance_DefaultConstructedClock_ThrowsInvalidOperation()
    {
        var clock = default(FixedStepClock);

        Assert.Throws<InvalidOperationException>(() => clock.Advance(16));
    }
}
