using System.Reflection;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-3 re-pointed these at <c>MW3.Protocol</c>'s <see cref="HitTester"/>, which answers over
/// <see cref="BaseSnapshot"/> now that the renderer holds snapshots and not bases. The claims are
/// unchanged - the same threshold, the same nearest-wins rule, the same geometry assertion about the
/// shipped map - because the hit-test itself did not change; only the shape it reads did. They are
/// re-pointed rather than duplicated: two copies of a hit-test's expectations is exactly the drift
/// D-67 refuses.
/// </summary>
public class HitTesterTests
{
    private static IReadOnlyList<BaseSnapshot> Bases(Match match) =>
        MatchSnapshotBuilder.Build(match, match.HumanPlayer).Bases;

    private static BaseSnapshot HumanBase(Match match)
    {
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        return snapshot.Bases.Single(b => b.OwnerPlayerId == snapshot.LocalPlayerId);
    }

    [Fact]
    public void FindBaseAt_ExactCentreOfABase_ReturnsThatBase()
    {
        var match = new Match();
        var human = HumanBase(match);

        var result = HitTester.FindBaseAt(human.Position, Bases(match));

        Assert.Equal(human.Id, result);
    }

    [Fact]
    public void FindBaseAt_JustInsideTheThreshold_ReturnsTheNearestBase()
    {
        var match = new Match();
        var human = HumanBase(match);
        var offset = HitTester.SelectionThresholdUnits - 0.01;
        var point = new MapPoint(human.Position.X + offset, human.Position.Y);

        var result = HitTester.FindBaseAt(point, Bases(match));

        Assert.Equal(human.Id, result);
    }

    [Fact]
    public void FindBaseAt_JustOutsideTheThreshold_ReturnsNoBase()
    {
        var match = new Match();
        var human = HumanBase(match);
        var offset = HitTester.SelectionThresholdUnits + 0.01;
        var point = new MapPoint(human.Position.X + offset, human.Position.Y);

        var result = HitTester.FindBaseAt(point, Bases(match));

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

        var result = HitTester.FindBaseAt(new MapPoint(x, y), Bases(match));

        Assert.Null(result);
    }

    [Fact]
    public void FindNearestBaseId_PointBetweenTwoBases_ResolvesToTheGenuinelyNearerOne()
    {
        var match = new Match();
        var bases = Bases(match);
        var nearer = bases.Single(b => b.Position == new MapPoint(0.35, 0.25));
        var farther = bases.Single(b => b.Position == new MapPoint(0.65, 0.25));

        // Between the two (0.30 apart), but noticeably closer to `nearer`.
        var point = new MapPoint(0.40, 0.25);

        var method = typeof(HitTester).GetMethod("FindNearestBaseId", BindingFlags.NonPublic | BindingFlags.Static)!;
        var parameters = new object?[] { point, bases, null };
        var result = (int?)method.Invoke(null, parameters);

        Assert.Equal(nearer.Id, result);
        Assert.NotEqual(farther.Id, result);
    }

    // Re-authored at phase 6 FR-2: the neutral forge (id 6, at (0.50, 0.20)) and neutral tower
    // (id 7, at (0.50, 0.80)) placed on the centre line each sit 0.158 from their two nearest
    // bottom/top-flank neutrals - within twice HitTester.SelectionThresholdUnits (0.2) - the same
    // geometry LevelTableTests' Tower_EveryRange test had to accept rather than preserve. The
    // blanket "no two bases" claim is unpreservable with two drawable centre-line slots; it is
    // replaced, not weakened, by asserting every OTHER pair still clears the threshold and naming
    // the four known-close pairs explicitly, so a future position change cannot silently grow a
    // fifth ambiguous pair.
    [Fact]
    public void HardcodedMap_NoTwoBasesLieWithinTwiceTheThreshold_ExceptTheFourKnownCentreLinePairs()
    {
        var match = new Match();
        var bases = Bases(match);

        var knownClosePairs = new HashSet<(int, int)>
        {
            (2, 6), (4, 6), // the two top-flank neutrals to the neutral forge
            (3, 7), (5, 7), // the two bottom-flank neutrals to the neutral tower
        };

        for (var i = 0; i < bases.Count; i++)
        {
            for (var j = i + 1; j < bases.Count; j++)
            {
                var dx = bases[i].Position.X - bases[j].Position.X;
                var dy = bases[i].Position.Y - bases[j].Position.Y;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var pair = (bases[i].Id, bases[j].Id);

                if (knownClosePairs.Contains(pair))
                {
                    Assert.True(
                        distance <= 2 * HitTester.SelectionThresholdUnits,
                        $"Base {pair.Item1} and base {pair.Item2} were expected to be a known-close centre-line pair but are {distance} apart.");
                    continue;
                }

                Assert.True(
                    distance > 2 * HitTester.SelectionThresholdUnits,
                    $"Base {bases[i].Id} and base {bases[j].Id} are only {distance} apart - within twice the threshold.");
            }
        }

        Assert.Equal(4, knownClosePairs.Count);
    }
}
