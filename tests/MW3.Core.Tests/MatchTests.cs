using System.Reflection;

namespace MW3.Core.Tests;

public class MatchTests
{
    [Fact]
    public void Constants_MatchTheAgreedTickDuration()
    {
        Assert.Equal(50, Match.TickDurationMilliseconds);
    }

    /// <summary>
    /// Pinned against the phase-6 shipped board (D-49), preserved as a fixture - the parameterless
    /// constructor now defaults to <see cref="MapCatalog.Small"/>, six bases (FR-2).
    /// </summary>
    [Fact]
    public void Constructor_CreatesExactlyEightBasesAtTheAgreedPositions()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);

        Assert.Equal(8, match.Bases.Count);

        var positions = match.Bases.Select(b => (b.Position.X, b.Position.Y)).ToArray();
        Assert.Contains((0.12, 0.50), positions);
        Assert.Contains((0.88, 0.50), positions);
        Assert.Contains((0.35, 0.25), positions);
        Assert.Contains((0.35, 0.75), positions);
        Assert.Contains((0.65, 0.25), positions);
        Assert.Contains((0.65, 0.75), positions);
        Assert.Contains((0.50, 0.20), positions);
        Assert.Contains((0.50, 0.80), positions);
    }

    [Fact]
    public void Constructor_HumanBaseIsAtItsAgreedPosition_AndAiBaseIsAtItsAgreedPosition()
    {
        var match = new Match();

        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);
        var aiBase = Assert.Single(match.Bases, b => b.Owner == match.AiPlayer);

        Assert.Equal((0.12, 0.50), (humanBase.Position.X, humanBase.Position.Y));
        Assert.Equal((0.88, 0.50), (aiBase.Position.X, aiBase.Position.Y));
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

    // Re-authored at phase 6 FR-2: the layout gains two more neutrals - the neutral forge and
    // neutral tower (ids 6, 7) - each starting at garrison 10, double an ordinary neutral's 5
    // (REQUIREMENTS.md §4 "Tuning values"). The four original flank neutrals are unchanged.
    [Fact]
    public void Constructor_HumanAndAiBasesAreOwnedWithGarrisonTen_OriginalNeutralsAreOwnerlessWithGarrisonFive_NewNeutralsWithGarrisonTen()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);

        var humanBase = Assert.Single(match.Bases, b => b.Owner == match.HumanPlayer);
        var aiBase = Assert.Single(match.Bases, b => b.Owner == match.AiPlayer);
        var originalNeutrals = match.Bases.Where(b => b.Owner is null && b.Type == BaseType.Producer).ToArray();
        var newNeutrals = match.Bases.Where(b => b.Owner is null && b.Type != BaseType.Producer).ToArray();

        Assert.Equal(10, humanBase.GarrisonCount);
        Assert.Equal(10, aiBase.GarrisonCount);
        Assert.Equal(4, originalNeutrals.Length);
        Assert.All(originalNeutrals, b => Assert.Equal(5, b.GarrisonCount));
        Assert.Equal(2, newNeutrals.Length);
        Assert.All(newNeutrals, b => Assert.Equal(10, b.GarrisonCount));
    }

    [Fact]
    public void Advance_SixHundredTicks_HumanAndAiBasesBothReachTheLevelOneCapOfTwenty()
    {
        // Production actually happens, and both sides get identical treatment: (20-10) units at
        // 60 ticks/unit reaches the level-1 cap of 20 in exactly 600 ticks.
        var match = new Match();

        match.Advance(600);

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
    // Re-authored at phase 6 FR-2: the neutral forge and neutral tower (ids 6, 7) start at garrison
    // 10, not 5, so this is split by type rather than asserted as one blanket 5 across every
    // neutral base.
    public void Advance_NeutralBases_NeverProduceEvenAfterOneThousandTicks()
    {
        var match = new Match(PhaseSixEightSlotFixture.Slots);

        match.Advance(1000);

        var originalNeutrals = match.Bases.Where(b => b.Owner is null && b.Type == BaseType.Producer);
        Assert.All(originalNeutrals, b => Assert.Equal(5, b.GarrisonCount));

        var newNeutrals = match.Bases.Where(b => b.Owner is null && b.Type != BaseType.Producer);
        Assert.All(newNeutrals, b => Assert.Equal(10, b.GarrisonCount));
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
