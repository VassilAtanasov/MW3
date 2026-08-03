using MW3.Core;

namespace MW3.Game.Tests;

/// <summary>
/// Headless coverage of FR-4/D-36's pure geometry and timing helper - no graphics device, mirroring
/// how <see cref="SendStrengthSelectorTests"/> exercises <see cref="SendStrengthSelector"/>. The
/// grouping tests build real armies through <see cref="Match"/> (as <see cref="Core.SendWaveTests"/>-
/// style tests already do in MW3.Core.Tests) rather than synthesizing them, since <see cref="Army"/>'s
/// constructor is internal to MW3.Core and not visible here.
/// </summary>
public class WaveColumnPresentationTests
{
    private const float _lead = 0.08f;
    private const float _trailing = 0.04f;

    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    // --- RadiusFraction ---

    [Fact]
    public void RadiusFraction_SingleWaveSend_ReturnsLeadFractionUnchanged()
    {
        Assert.Equal(_lead, WaveColumnPresentation.RadiusFraction(waveIndex: 1, waveCount: 1, _lead, _trailing));
    }

    [Theory]
    [InlineData(1, 2, 0.08f)]
    [InlineData(2, 2, 0.04f)]
    [InlineData(1, 3, 0.08f)]
    [InlineData(2, 3, 0.06f)]
    [InlineData(3, 3, 0.04f)]
    [InlineData(1, 10, 0.08f)]
    [InlineData(10, 10, 0.04f)]
    public void RadiusFraction_TapersLinearlyFromLeadToTrailing(int waveIndex, int waveCount, float expected)
    {
        var result = WaveColumnPresentation.RadiusFraction(waveIndex, waveCount, _lead, _trailing);

        Assert.Equal(expected, result, 5);
    }

    [Fact]
    public void RadiusFraction_MidWaveOfTen_InterpolatesLinearlyByFractionalIndex()
    {
        // wave 5 of 10: t = (5-1)/(10-1) = 4/9
        var result = WaveColumnPresentation.RadiusFraction(waveIndex: 5, waveCount: 10, _lead, _trailing);

        Assert.Equal(0.0622222f, result, 5);
    }

    // --- IsFlashing ---

    [Fact]
    public void IsFlashing_FalseWhenTheEventNeverHappened()
    {
        Assert.False(WaveColumnPresentation.IsFlashing(elapsedTicks: 100, eventTick: null, durationTicks: 4));
    }

    [Theory]
    [InlineData(100, 100, 4, true)] // the firing tick itself
    [InlineData(103, 100, 4, true)] // one tick before the duration elapses
    [InlineData(104, 100, 4, false)] // exactly the duration elapsed - no longer flashing
    [InlineData(200, 100, 4, false)] // long past
    public void IsFlashing_TrueOnlyWithinDurationTicksOfTheEvent(long elapsedTicks, long eventTick, int durationTicks, bool expected)
    {
        Assert.Equal(expected, WaveColumnPresentation.IsFlashing(elapsedTicks, eventTick, durationTicks));
    }

    // --- ComputeSpineSegments ---

    [Fact]
    public void ComputeSpineSegments_SingleWaveSend_ProducesNoSegments()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 1);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 1)));

        var output = new List<(int FromIndex, int ToIndex)>();
        WaveColumnPresentation.ComputeSpineSegments(match.ArmiesInFlight, output);

        Assert.Empty(output);
    }

    [Fact]
    public void ComputeSpineSegments_GroupsBySendIdNotByAdjacency_WhenASecondSendInterleavesMidColumn()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 30);

        // Send A: 12 units -> waves of 8 then 4 (SendWaveCalculator), wave 1 launches immediately.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 12)));
        var sendAId = match.ArmiesInFlight.Single().SendId;

        // One tick later, send B starts from the same base: its own wave 1 launches immediately too,
        // landing in ArmiesInFlight between send A's wave 1 (already there) and wave 2 (still 4 ticks
        // away) - the interleaving the acceptance criterion names explicitly.
        match.Advance(1);
        SetGarrison(human, 30);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 12)));
        var sendBId = match.ArmiesInFlight.Single(a => a.SendId != sendAId).SendId;

        // Advance onto send A's wave 2 launch tick (5): send B's wave 2 has not launched yet (its
        // send started one tick later, so its wave 2 launches at tick 6). At this exact tick,
        // ArmiesInFlight holds send A's wave 1 and wave 2, and only send B's wave 1 - a genuine
        // interleaving of a two-member group and a one-member group.
        match.Advance(4);

        var armies = match.ArmiesInFlight;
        Assert.Equal(3, armies.Count);
        Assert.Equal(2, armies.Count(a => a.SendId == sendAId));
        Assert.Single(armies, a => a.SendId == sendBId);

        var output = new List<(int FromIndex, int ToIndex)>();
        WaveColumnPresentation.ComputeSpineSegments(armies, output);

        var segment = Assert.Single(output);
        var from = armies[segment.FromIndex];
        var to = armies[segment.ToIndex];
        Assert.Equal(sendAId, from.SendId);
        Assert.Equal(sendAId, to.SendId);
        Assert.Equal(1, from.WaveIndex);
        Assert.Equal(2, to.WaveIndex);
    }

    [Fact]
    public void ComputeSpineSegments_PartiallyArrivedSend_LinksOnlyTheWavesStillInFlight()
    {
        var match = new Match();
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 20);

        // 20 units -> three waves (8, 8, 4), launched at ticks 0, 5, 10, each with the same fixed
        // 34-tick travel time (SendArmyTests pins this), so they arrive at 34, 39, 44 respectively -
        // every wave has already launched by the time wave 1 arrives.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 20)));
        Assert.Equal(3, SendWaveCalculator.WaveCount(20));

        var wave1 = match.ArmiesInFlight.Single(a => a.WaveIndex == 1);
        match.Advance(wave1.ArrivalTick - match.ElapsedTicks); // wave 1 arrives and leaves ArmiesInFlight

        var armies = match.ArmiesInFlight;
        Assert.Equal(2, armies.Count); // only waves 2 and 3 remain in flight

        var output = new List<(int FromIndex, int ToIndex)>();
        WaveColumnPresentation.ComputeSpineSegments(armies, output);

        var segment = Assert.Single(output);
        Assert.Equal(2, armies[segment.FromIndex].WaveIndex);
        Assert.Equal(3, armies[segment.ToIndex].WaveIndex);
    }
}
