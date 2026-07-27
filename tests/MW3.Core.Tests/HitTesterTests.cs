using System.Reflection;

namespace MW3.Core.Tests;

public class HitTesterTests
{
    [Fact]
    public void FindBaseAt_ExactCentreOfABase_ReturnsThatBase()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);

        var result = HitTester.FindBaseAt(human.Position, match.Bases);

        Assert.Equal(human.Id, result);
    }

    [Fact]
    public void FindBaseAt_JustInsideTheThreshold_ReturnsTheNearestBase()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var offset = HitTester.SelectionThresholdUnits - 0.01;
        var point = new MapPoint(human.Position.X + offset, human.Position.Y);

        var result = HitTester.FindBaseAt(point, match.Bases);

        Assert.Equal(human.Id, result);
    }

    [Fact]
    public void FindBaseAt_JustOutsideTheThreshold_ReturnsNoBase()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var offset = HitTester.SelectionThresholdUnits + 0.01;
        var point = new MapPoint(human.Position.X + offset, human.Position.Y);

        var result = HitTester.FindBaseAt(point, match.Bases);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 1.0)]
    public void FindBaseAt_MapCorners_ReturnNoBase(double x, double y)
    {
        var match = new Match();

        var result = HitTester.FindBaseAt(new MapPoint(x, y), match.Bases);

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestBaseId_PointBetweenTwoBases_ResolvesToTheGenuinelyNearerOne()
    {
        var match = new Match();
        var nearer = match.Bases.Single(b => b.Position == new MapPoint(0.35, 0.25));
        var farther = match.Bases.Single(b => b.Position == new MapPoint(0.65, 0.25));

        // Between the two (0.30 apart), but noticeably closer to `nearer`.
        var point = new MapPoint(0.40, 0.25);

        var method = typeof(HitTester).GetMethod("FindNearestBaseId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = new object?[] { point, match.Bases, null };
        var result = (int?)method.Invoke(null, parameters);

        Assert.Equal(nearer.Id, result);
        Assert.NotEqual(farther.Id, result);
    }

    [Fact]
    public void HardcodedMap_NoTwoBasesLieWithinTwiceTheThreshold_SoTheNearestMatchIsNeverAmbiguous()
    {
        var match = new Match();
        var bases = match.Bases;

        for (var i = 0; i < bases.Count; i++)
        {
            for (var j = i + 1; j < bases.Count; j++)
            {
                var dx = bases[i].Position.X - bases[j].Position.X;
                var dy = bases[i].Position.Y - bases[j].Position.Y;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));

                Assert.True(
                    distance > 2 * HitTester.SelectionThresholdUnits,
                    $"Base {bases[i].Id} and base {bases[j].Id} are only {distance} apart - within twice the threshold.");
            }
        }
    }
}
