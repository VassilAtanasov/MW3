namespace MW3.Core.Tests;

/// <summary>
/// The cap as a production ceiling (D-21): it stops a base growing on its own, and does nothing
/// else - it never destroys a unit that arrives from elsewhere.
/// </summary>
public class GarrisonCapTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    [Fact]
    public void EveryBase_StartsAtTheMinimumLevel()
    {
        var match = new Match();

        Assert.All(match.Bases, b => Assert.Equal(LevelTable.MinLevel, b.Level));
    }

    [Fact]
    public void GarrisonCap_ReflectsTheBasesLevel()
    {
        var match = new Match();

        Assert.All(match.Bases, b => Assert.Equal(LevelTable.GarrisonCap(LevelTable.MinLevel), b.GarrisonCap));
    }

    [Fact]
    public void OwnedBase_ProducesUpToItsCapAndStopsThere()
    {
        var match = new Match();

        match.Advance(1000);

        var humanBase = HumanBase(match);
        Assert.Equal(LevelTable.GarrisonCap(LevelTable.MinLevel), humanBase.GarrisonCount);
        Assert.Equal(20, humanBase.GarrisonCount);
    }

    [Fact]
    public void NeutralBases_NeverProduce_EvenAfterOneThousandTicks()
    {
        var match = new Match();

        match.Advance(1000);

        Assert.All(match.Bases.Where(b => b.Owner is null), b => Assert.Equal(5, b.GarrisonCount));
    }

    [Fact]
    public void HeldAtCap_ThenDrained_TakesAFullPeriodToProduceAgain_RatherThanBankingUnits()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        match.Advance(100); // reaches the cap of 20 exactly
        Assert.Equal(20, humanBase.GarrisonCount);

        match.Advance(500); // held at the cap: nothing may accumulate here
        Assert.Equal(20, humanBase.GarrisonCount);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 5)));
        Assert.Equal(15, humanBase.GarrisonCount);

        var period = LevelTable.ProductionPeriodTicks(LevelTable.MinLevel);
        match.Advance(period - 1);
        Assert.Equal(15, humanBase.GarrisonCount); // no banked unit popped out

        match.Advance(1);
        Assert.Equal(16, humanBase.GarrisonCount); // exactly one full period later
    }

    [Fact]
    public void PushedToCapByAnArrival_ThenDrained_AlsoTakesAFullPeriod_NotJustTheProducedCase()
    {
        // The other half of the "held at cap" rule. Reaching the cap by *producing* leaves progress
        // at zero for free; reaching it by *arrival* is the path that can silently bank progress,
        // and it is a designed path - D-21 exists so a player can mass units on a staging base.
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Take a neutral, then let the capital refill while the captured base idles at 5.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        match.Advance(200);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        // Drain the captured base to a garrison well below its cap, and leave it partway through a
        // production period.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 15)));
        match.Advance(9);
        Assert.Equal(9, neutral.ProductionProgressTicks);
        Assert.True(neutral.GarrisonCount < neutral.GarrisonCap);

        // Now push it to its cap with an arrival rather than with production.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 20)));
        match.Advance(500);
        Assert.True(neutral.GarrisonCount >= neutral.GarrisonCap);
        Assert.Equal(0, neutral.ProductionProgressTicks); // nothing banked while capped

        // Drain it below the cap again: the next unit is a full period away, exactly as it is for a
        // base that reached its cap by producing.
        var period = LevelTable.ProductionPeriodTicks(LevelTable.MinLevel);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 20)));
        var afterDrain = neutral.GarrisonCount;
        Assert.True(afterDrain < neutral.GarrisonCap);

        match.Advance(period - 1);
        Assert.Equal(afterDrain, neutral.GarrisonCount);
        match.Advance(1);
        Assert.Equal(afterDrain + 1, neutral.GarrisonCount);
    }

    [Fact]
    public void ReinforcedToCapAndDrainedOnTheSameTick_StillTakesAFullPeriod_WithNoAdvanceInBetween()
    {
        // The half of the fix the test above does not reach. There, the arrival lands *inside* an
        // Advance, so the next production segment would zero the progress even without the write-site
        // fix in Match.ResolveArrival. Here the drain happens at the same elapsed tick as the
        // arrival, with no Advance between them - reachable from real input, because MatchScreen
        // advances the match and then processes a released drag within the same frame. Only the
        // write-site zeroing saves this case.
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);
        var period = LevelTable.ProductionPeriodTicks(LevelTable.MinLevel);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        match.Advance(200);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        // Leave the staging base below its cap and partway through a period.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 15)));
        match.Advance(4);
        Assert.True(neutral.ProductionProgressTicks > 0);
        Assert.True(neutral.GarrisonCount < neutral.GarrisonCap);

        // Reinforce it past its cap, and stop the advance exactly on the arrival tick.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 20)));
        // The earlier drain is still in flight toward the capital, so pick the reinforcement by its
        // target rather than assuming it is the only army on the map.
        var army = match.ArmiesInFlight.Single(a => a.TargetBaseId == neutral.Id);
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(army.ArrivalTick, match.ElapsedTicks);
        Assert.True(neutral.GarrisonCount >= neutral.GarrisonCap);
        Assert.Equal(0, neutral.ProductionProgressTicks); // zeroed by the arrival itself

        // Drain it on that same tick - no Advance has run since the arrival resolved.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, neutral.Id, humanBase.Id, 20)));
        var afterDrain = neutral.GarrisonCount;
        Assert.True(afterDrain < neutral.GarrisonCap);

        match.Advance(period - 1);
        Assert.Equal(afterDrain, neutral.GarrisonCount);
        match.Advance(1);
        Assert.Equal(afterDrain + 1, neutral.GarrisonCount);
    }

    [Fact]
    public void GarrisonAboveCap_IsNeverDestroyed_AndSimplyDoesNotProduce()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Take a neutral, then feed everything back into it from the capital so the captured base
        // is pushed above its own cap by arrivals rather than by production.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        match.Advance(200);
        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(20, neutral.GarrisonCount);
        Assert.Equal(20, humanBase.GarrisonCount);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 20)));
        match.Advance(200);

        Assert.True(neutral.GarrisonCount > neutral.GarrisonCap);
        Assert.Equal(40, neutral.GarrisonCount); // all 20 arrived; not one unit was clamped away

        var overCap = neutral.GarrisonCount;
        match.Advance(500);
        Assert.Equal(overCap, neutral.GarrisonCount); // above cap it produces nothing
    }

    [Fact]
    public void Production_IsPerBase_NotAGlobalTickBoundary()
    {
        // A base captured mid-match accumulates from its own capture, not from a global schedule:
        // it produces one period after it changed hands, whatever the match's absolute tick is.
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // 35 + a 17-tick flight lands the capture on tick 52 - deliberately not a multiple of the
        // production period, so a base still credited from global tick boundaries would produce at
        // tick 60 (8 ticks later) instead of 62.
        match.Advance(35);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));

        var army = Assert.Single(match.ArmiesInFlight);
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(52, match.ElapsedTicks);

        var atCapture = neutral.GarrisonCount;
        var period = LevelTable.ProductionPeriodTicks(LevelTable.MinLevel);

        match.Advance(period - 1);
        Assert.Equal(atCapture, neutral.GarrisonCount);

        match.Advance(1);
        Assert.Equal(atCapture + 1, neutral.GarrisonCount);
    }

    [Fact]
    public void LevelAndCap_HaveNoPublicSetter()
    {
        Assert.Null(typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: false));
        Assert.Null(typeof(Base).GetProperty(nameof(Base.GarrisonCap))!.GetSetMethod(nonPublic: false));
        Assert.Null(typeof(Base).GetProperty(nameof(Base.ProductionProgressTicks))!.GetSetMethod(nonPublic: false));
    }
}
