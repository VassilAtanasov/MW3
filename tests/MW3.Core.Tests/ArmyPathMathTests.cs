namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 D-68: army position and progress are one implementation, in <c>MW3.Protocol</c>, that
/// <see cref="Match.PositionOf"/> and <see cref="Match.ProgressOf"/> delegate to. The claim these
/// tests protect is not "the arithmetic is right" - <c>MatchPositionOfTests</c> and
/// <c>ObstacleDetourTests</c> already say that, and pass unchanged through this move. It is that the
/// two callers cannot come apart, which is the bug that would look like a rendering glitch and
/// would not be one.
/// </summary>
public class ArmyPathMathTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Theory]
    [InlineData(0, 10, 0, 0.0)]
    [InlineData(0, 10, 5, 0.5)]
    [InlineData(0, 10, 10, 1.0)]
    [InlineData(20, 40, 30, 0.5)]
    public void ProgressAt_IsTheElapsedFractionOfTheFlight(long launch, long arrival, long now, double expected) =>
        Assert.Equal(expected, ArmyPathMath.ProgressAt(launch, arrival, now));

    [Theory]
    [InlineData(-5, 0.0)]
    [InlineData(100, 1.0)]
    public void ProgressAt_ClampsRatherThanExtrapolating(long now, double expected) =>
        Assert.Equal(expected, ArmyPathMath.ProgressAt(0, 10, now));

    [Fact]
    public void ProgressAt_TreatsAZeroLengthFlightAsArrived() =>
        Assert.Equal(1.0, ArmyPathMath.ProgressAt(40, 40, 40));

    [Fact]
    public void PositionAtProgress_HitsTheEndpointsExactly()
    {
        var path = new ArmyPath(new[] { new MapPoint(0.1, 0.2), new MapPoint(0.4, 0.6), new MapPoint(0.9, 0.6) }, length: 1.0);

        Assert.Equal(new MapPoint(0.1, 0.2), ArmyPathMath.PositionAtProgress(path, 0.0));
        Assert.Equal(new MapPoint(0.9, 0.6), ArmyPathMath.PositionAtProgress(path, 1.0));
    }

    [Fact]
    public void PositionAtProgress_WalksTheWaypointsByArcLength()
    {
        // Two segments, 0.4 then 0.6, total 1.0. At fraction 0.4 the army is exactly on the corner;
        // at 0.7 it is half way along the second segment.
        var path = new ArmyPath(new[] { new MapPoint(0.0, 0.0), new MapPoint(0.4, 0.0), new MapPoint(0.4, 0.6) }, length: 1.0);

        Assert.Equal(new MapPoint(0.4, 0.0), ArmyPathMath.PositionAtProgress(path, 0.4));

        // Compared to 12 decimal places rather than exactly: walking two segments accumulates the
        // usual double arithmetic, and 0.3 arrives as 0.29999999999999993. Determinism (D-60) is
        // the claim that every run produces the SAME bits, not that they are the decimal ideal.
        var midway = ArmyPathMath.PositionAtProgress(path, 0.7);
        Assert.Equal(0.4, midway.X, 12);
        Assert.Equal(0.3, midway.Y, 12);
    }

    [Fact]
    public void PositionAtProgress_RejectsANullPath() =>
        Assert.Throws<ArgumentNullException>(() => ArmyPathMath.PositionAtProgress(null!, 0.5));

    [Fact]
    public void MatchPositionOfAndProgressOf_AgreeWithTheSharedFunctionsAtEveryTickOfAFlight()
    {
        var match = new Match(MapCatalog.Medium);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = Assert.Single(match.ArmiesInFlight);

        while (match.ArmiesInFlight.Count > 0)
        {
            Assert.Equal(ArmyPathMath.ProgressAt(army.LaunchTick, army.ArrivalTick, match.ElapsedTicks), match.ProgressOf(army));
            Assert.Equal(
                ArmyPathMath.PositionAt(army.Path, army.LaunchTick, army.ArrivalTick, match.ElapsedTicks),
                match.PositionOf(army));
            match.Advance(1);
        }
    }
}
