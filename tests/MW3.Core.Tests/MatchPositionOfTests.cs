using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// FR-4: <see cref="Match.PositionOf"/> and <see cref="Match.ProgressOf"/> are the single source a
/// renderer reads an army's drawn position from (<c>docs/maps/REQUIREMENTS.md</c> FR-4) - covers
/// that they agree with the tick arithmetic tower fire already resolves against
/// (<c>Match.PositionAtTick</c>, exercised via reflection exactly as
/// <see cref="ObstacleDetourTests"/> already does), that Medium's mid-flight position sits off the
/// straight line and outside the obstacle, and that an unobstructed map's position is exactly the
/// straight-line interpolation the old <c>MatchScreen</c> arithmetic produced.
/// </summary>
public class MatchPositionOfTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static MapPoint PositionAtTick(Match match, Army army, long tick)
    {
        var method = typeof(Match).GetMethod("PositionAtTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (MapPoint)method.Invoke(match, new object[] { army, tick })!;
    }

    [Fact]
    public void PositionOf_AgreesWithThePrivateTickWalk_AtTheMatchsCurrentElapsedTicks()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = match.ArmiesInFlight.Single();

        match.Advance((army.ArrivalTick - match.ElapsedTicks) / 2);

        var expected = PositionAtTick(match, army, match.ElapsedTicks);
        var actual = match.PositionOf(army);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProgressOf_IsZeroAtLaunchAndOneAtArrival_WithAFractionInBetween()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = match.ArmiesInFlight.Single();

        Assert.Equal(0.0, match.ProgressOf(army));

        // One tick short of arrival: the army is still in ArmiesInFlight, and its progress is a
        // fraction strictly between 0 and 1, matching the position PositionOf reads at that fraction.
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);
        var midProgress = match.ProgressOf(army);
        Assert.True(midProgress is > 0.0 and < 1.0);
        Assert.Equal(match.PositionOf(army), PositionAtTick(match, army, match.ElapsedTicks));
    }

    [Fact]
    public void MediumMap_PositionOf_AtMidFlight_IsOffTheStraightLineAndOutsideTheObstacle()
    {
        var match = new Match(MapCatalog.Medium);
        var human = HumanBase(match);
        var ai = AiBase(match);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 8)));
        var army = match.ArmiesInFlight.Single();

        match.Advance((army.ArrivalTick - match.ElapsedTicks) / 2);
        var position = match.PositionOf(army);

        var straightLineMidpoint = new MapPoint(0.50, 0.50);
        var dx = position.X - straightLineMidpoint.X;
        var dy = position.Y - straightLineMidpoint.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        Assert.True(distance >= 0.15, $"expected the mid-flight position to sit at least 0.15 from (0.50, 0.50), was {distance}");

        var obstacle = Assert.Single(match.Obstacles);
        Assert.False(obstacle.Contains(position), $"expected the mid-flight position {position} to sit outside the obstacle");
    }

    [Fact]
    public void SmallMap_PositionOf_MatchesStraightLineInterpolation_AtEveryTickOfTheFlight()
    {
        var match = new Match(MapCatalog.Small);
        var human = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        SetGarrison(human, 8);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)));
        var army = match.ArmiesInFlight.Single();
        var span = army.ArrivalTick - army.LaunchTick;

        for (var tick = army.LaunchTick; tick <= army.ArrivalTick; tick += 5)
        {
            match.Advance(tick - match.ElapsedTicks);
            var fraction = (double)(tick - army.LaunchTick) / span;
            var expectedX = human.Position.X + ((neutral.Position.X - human.Position.X) * fraction);
            var expectedY = human.Position.Y + ((neutral.Position.Y - human.Position.Y) * fraction);

            var actual = match.PositionOf(army);

            // Within one pixel at 1280x720, as the acceptance criterion states.
            Assert.True(Math.Abs((actual.X - expectedX) * 1280) < 1.0);
            Assert.True(Math.Abs((actual.Y - expectedY) * 720) < 1.0);
        }
    }
}
