using System.Reflection;

namespace MW3.Core.Tests;

public class MatchTests
{
    [Fact]
    public void Constants_MatchTheAgreedTickDurationAndProductionPeriod()
    {
        Assert.Equal(100, Match.TickDurationMilliseconds);
        Assert.Equal(10, Match.ProductionPeriodTicks);
    }

    [Fact]
    public void Constructor_CreatesExactlySixBasesAtTheAgreedPositions()
    {
        var match = new Match();

        Assert.Equal(6, match.Bases.Count);

        var positions = match.Bases.Select(b => (b.Position.X, b.Position.Y)).ToArray();
        Assert.Contains((0.12, 0.50), positions);
        Assert.Contains((0.88, 0.50), positions);
        Assert.Contains((0.35, 0.25), positions);
        Assert.Contains((0.35, 0.75), positions);
        Assert.Contains((0.65, 0.25), positions);
        Assert.Contains((0.65, 0.75), positions);
    }

    [Fact]
    public void Constructor_EveryBasePositionIsWithinTheNormalizedRange()
    {
        var match = new Match();

        foreach (var b in match.Bases)
        {
            Assert.InRange(b.Position.X, 0.0, 1.0);
            Assert.InRange(b.Position.Y, 0.0, 1.0);
        }
    }

    [Fact]
    public void Constructor_HumanAndAiBasesAreOwnedWithGarrisonTen_NeutralsAreOwnerlessWithGarrisonFive()
    {
        var match = new Match();

        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);
        var aiBase = Assert.Single(match.Bases, b => b.Owner == match.AiPlayer);
        var neutralBases = match.Bases.Where(b => b.Owner is null).ToArray();

        Assert.Equal(10, humanBase.GarrisonCount);
        Assert.Equal(10, aiBase.GarrisonCount);
        Assert.Equal(4, neutralBases.Length);
        Assert.All(neutralBases, b => Assert.Equal(5, b.GarrisonCount));
    }

    [Fact]
    public void Advance_OneHundredTicks_HumanAndAiBasesEachGainTwentyUnitsTotal()
    {
        var match = new Match();

        match.Advance(100);

        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);
        var aiBase = Assert.Single(match.Bases, b => b.Owner == match.AiPlayer);
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(20, aiBase.GarrisonCount);
    }

    [Fact]
    public void Advance_SevenThenThreeTicks_EqualsTenTicksInOneCall()
    {
        var chunked = new Match();
        chunked.Advance(7);
        chunked.Advance(3);

        var single = new Match();
        single.Advance(10);

        var chunkedHuman = Assert.Single(chunked.Bases, b => b.Owner == chunked.HumanPlayer);
        var singleHuman = Assert.Single(single.Bases, b => b.Owner == single.HumanPlayer);
        Assert.Equal(singleHuman.GarrisonCount, chunkedHuman.GarrisonCount);
    }

    [Fact]
    public void Advance_NineTicksFromFreshMatch_AddsNoUnit()
    {
        var match = new Match();

        match.Advance(9);

        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);
        Assert.Equal(10, humanBase.GarrisonCount);
    }

    [Fact]
    public void Advance_NeutralBases_NeverProduceEvenAfterOneThousandTicks()
    {
        var match = new Match();

        match.Advance(1000);

        var neutralBases = match.Bases.Where(b => b.Owner is null);
        Assert.All(neutralBases, b => Assert.Equal(5, b.GarrisonCount));
    }

    [Fact]
    public void Advance_Zero_LeavesEveryGarrisonUnchanged()
    {
        var match = new Match();
        var before = match.Bases.Select(b => b.GarrisonCount).ToArray();

        match.Advance(0);

        var after = match.Bases.Select(b => b.GarrisonCount).ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Advance_NegativeTicks_ThrowsArgumentOutOfRangeException()
    {
        var match = new Match();

        Assert.Throws<ArgumentOutOfRangeException>(() => match.Advance(-1));
    }

    [Fact]
    public void Advance_SameTotalTicksInDifferentChunkSizes_IsDeterministicAcrossAllBases()
    {
        var oneCall = new Match();
        oneCall.Advance(137);

        var manyCalls = new Match();
        foreach (var chunk in new long[] { 1, 4, 2, 10, 20, 100 })
        {
            manyCalls.Advance(chunk);
        }

        var oneCallGarrisons = oneCall.Bases.Select(b => b.GarrisonCount).ToArray();
        var manyCallsGarrisons = manyCalls.Bases.Select(b => b.GarrisonCount).ToArray();
        Assert.Equal(oneCallGarrisons, manyCallsGarrisons);
    }

    [Fact]
    public void Bases_PropertyType_IsReadOnly_NotAMutableCollectionType()
    {
        var propertyType = typeof(Match).GetProperty(nameof(Match.Bases))!.PropertyType;

        Assert.Equal(typeof(IReadOnlyList<Base>), propertyType);
    }

    [Fact]
    public void PublicSurface_ExposesNoSettableProperty()
    {
        var properties = typeof(Match).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            Assert.Null(property.GetSetMethod(nonPublic: false));
        }
    }
}
