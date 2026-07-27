using System.Reflection;

namespace MW3.Core.Tests;

public class SendArmyTests
{
    private static (Match Match, Base Human, Base Ai, Base[] Neutrals) NewMatch()
    {
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var neutrals = match.Bases.Where(b => b.Owner is null).ToArray();
        return (match, human, ai, neutrals);
    }

    // --- The command and its validation ---

    [Fact]
    public void Execute_SourceNotOwnedByIssuer_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, ai, _) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, ai.Id, human.Id, 1));

        Assert.Equal(SendArmyOutcome.SourceNotOwnedByIssuer, outcome);
        Assert.Equal(10, ai.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_SourceEqualsTarget_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, _, _) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, human.Id, 1));

        Assert.Equal(SendArmyOutcome.SourceEqualsTarget, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_UnitCountZero_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 0));

        Assert.Equal(SendArmyOutcome.UnitCountNotPositive, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_UnitCountNegative_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, -1));

        Assert.Equal(SendArmyOutcome.UnitCountNotPositive, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_UnitCountExceedsGarrison_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 11));

        Assert.Equal(SendArmyOutcome.UnitCountExceedsGarrison, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_SourceBaseIdDoesNotExist_RejectedAndLeavesStateUnchanged()
    {
        var (match, _, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, 999, neutrals[0].Id, 1));

        Assert.Equal(SendArmyOutcome.BaseNotFound, outcome);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_TargetBaseIdDoesNotExist_RejectedAndLeavesStateUnchanged()
    {
        var (match, human, _, _) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, 999, 1));

        Assert.Equal(SendArmyOutcome.BaseNotFound, outcome);
        Assert.Equal(10, human.GarrisonCount);
        Assert.Empty(match.ArmiesInFlight);
    }

    [Fact]
    public void Execute_Accepted_SubtractsExactlyTheRequestedCountFromSource()
    {
        var (match, human, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 4));

        Assert.Equal(SendArmyOutcome.Accepted, outcome);
        Assert.Equal(6, human.GarrisonCount);
    }

    [Fact]
    public void Execute_SendingEntireGarrison_IsAccepted_AndBaseSitsAtZeroStillOwned()
    {
        var (match, human, _, neutrals) = NewMatch();

        var outcome = match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 10));

        Assert.Equal(SendArmyOutcome.Accepted, outcome);
        Assert.Equal(0, human.GarrisonCount);
        Assert.Equal(match.HumanPlayer, human.Owner);
    }

    [Fact]
    public void ZeroGarrisonOwnedBase_StillProduces()
    {
        var (match, human, _, neutrals) = NewMatch();
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 10));
        Assert.Equal(0, human.GarrisonCount);

        match.Advance(Match.ProductionPeriodTicks);

        Assert.Equal(1, human.GarrisonCount);
    }

    [Fact]
    public void BaseAtZeroGarrison_CanBeCapturedByOneUnit()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5)); // ties: 5 vs 5
        match.Advance(17);
        Assert.Null(neutral.Owner);
        Assert.Equal(0, neutral.GarrisonCount);

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 1)); // now 1 > 0
        match.Advance(17);

        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(1, neutral.GarrisonCount);
    }

    // --- Transit ---

    [Fact]
    public void ArmySpeedConstant_MatchesFullMapWidthInFiveSeconds()
    {
        Assert.Equal(0.02, Match.ArmySpeedUnitsPerTick);
        Assert.Equal(50, 1.0 / Match.ArmySpeedUnitsPerTick);
    }

    [Fact]
    public void TravelTime_HumanToNearestNeutral_IsRoughlySeventeenTicks()
    {
        var (match, human, _, neutrals) = NewMatch();
        var nearest = neutrals.OrderBy(n => Distance(human.Position, n.Position)).First();

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, nearest.Id, 1));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(17, army.ArrivalTick - army.LaunchTick);
    }

    [Fact]
    public void TravelTime_HumanToAiBase_IsRoughlyThirtyEightTicks()
    {
        var (match, human, ai, _) = NewMatch();

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 1));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(38, army.ArrivalTick - army.LaunchTick);
    }

    [Fact]
    public void TravelTime_IsNeverLessThanOneTick_HoweverCloseTwoBasesAre()
    {
        // The one hardcoded map has no pair closer than ~17 ticks apart, so the floor can only be
        // exercised by calling the private computation directly with a near-zero distance.
        var method = typeof(Match).GetMethod("ComputeTravelTicks", BindingFlags.NonPublic | BindingFlags.Static)!;

        var ticks = (long)method.Invoke(null, new object[] { new MapPoint(0.5, 0.5), new MapPoint(0.5, 0.5) })!;

        Assert.Equal(1, ticks);
    }

    [Fact]
    public void ArmiesInFlight_ExposesOwnerSourceTargetUnitCountLaunchAndArrivalTick()
    {
        var (match, human, _, neutrals) = NewMatch();

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 3));

        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(match.HumanPlayer, army.Owner);
        Assert.Equal(human.Id, army.SourceBaseId);
        Assert.Equal(neutrals[0].Id, army.TargetBaseId);
        Assert.Equal(3, army.UnitCount);
        Assert.Equal(0, army.LaunchTick);
        Assert.True(army.ArrivalTick > army.LaunchTick);
    }

    [Fact]
    public void ArmiesInFlight_PropertyType_IsReadOnly_NotAMutableCollectionType()
    {
        var propertyType = typeof(Match).GetProperty(nameof(Match.ArmiesInFlight))!.PropertyType;

        Assert.Equal(typeof(IReadOnlyList<Army>), propertyType);
    }

    [Fact]
    public void ArmyInFlight_IsInert_CapturingItsSourceDoesNotChangeItsOwnerOrDestination()
    {
        var (match, human, ai, neutrals) = NewMatch();

        match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, human.Id, 10)); // arrives tick 38

        match.Advance(1);
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 2)); // witness, arrives tick 39

        match.Advance(36); // elapsed 37
        var drainAmount = human.GarrisonCount - 1;
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, drainAmount)); // leaves 1 unit

        match.Advance(1); // elapsed 38: the AI's army captures the now nearly-empty human base

        Assert.Equal(match.AiPlayer, human.Owner);

        var witness = match.ArmiesInFlight.Single(a => a.TargetBaseId == ai.Id);
        Assert.Equal(match.HumanPlayer, witness.Owner);
        Assert.Equal(human.Id, witness.SourceBaseId);
        Assert.Equal(ai.Id, witness.TargetBaseId);
    }

    [Fact]
    public void SeveralArmiesInFlightAtOnce_FromSameSourceAndToSameTarget_AreAllTracked()
    {
        var (match, human, _, neutrals) = NewMatch();

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 1));
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[1].Id, 1));
        match.Advance(1);
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 1));

        Assert.Equal(3, match.ArmiesInFlight.Count);
    }

    [Fact]
    public void Army_IsRemovedFromInFlightView_InTheSameAdvanceCallThatResolvesIt()
    {
        var (match, human, _, neutrals) = NewMatch();
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutrals[0].Id, 1));

        match.Advance(17);

        Assert.Empty(match.ArmiesInFlight);
    }

    // --- Arrival ---

    [Fact]
    public void Arrival_AtOwnBase_AddsUnitsToGarrison()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 6));
        match.Advance(17); // captures: 6 > 5
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        var garrisonBeforeReinforcement = neutral.GarrisonCount;
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 2));
        match.Advance(17);

        Assert.True(neutral.GarrisonCount >= garrisonBeforeReinforcement + 2);
        Assert.Equal(match.HumanPlayer, neutral.Owner);
    }

    [Fact]
    public void Arrival_AttackerStrongerThanDefender_CapturesWithNMinusMUnits()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8));
        match.Advance(17);

        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(3, neutral.GarrisonCount);
    }

    [Fact]
    public void Arrival_AttackerWeakerThanDefender_IsRepelledWithMMinusNUnitsRemaining()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 3));
        match.Advance(17);

        Assert.Null(neutral.Owner);
        Assert.Equal(2, neutral.GarrisonCount);
    }

    [Fact]
    public void Arrival_AttackerEqualsDefender_DefenderKeepsBaseAtZero()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5));
        match.Advance(17);

        Assert.Null(neutral.Owner);
        Assert.Equal(0, neutral.GarrisonCount);
    }

    [Fact]
    public void Resolution_UsesTheTargetsOwnerAtArrival_NotAtLaunch()
    {
        // Human (17 ticks from the neutral) and AI (30 ticks from the same neutral) both launch at
        // tick 0. Human's army lands first and captures it. AI's army - launched when the base was
        // still neutral - lands later and, because ownership is read live, correctly attacks the
        // now-human base rather than treating it as an uncontested neutral capture. A further human
        // reinforcement, launched while the base was still its own, lands after the AI's later
        // recapture and - again reading live ownership - attacks instead of blindly reinforcing.
        var match = new Match();
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var neutral = match.Bases.Where(b => b.Owner is null)
            .OrderBy(n => Distance(human.Position, n.Position))
            .First();

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 8)); // arrives tick 17
        match.Execute(new SendArmyCommand(match.AiPlayer, ai.Id, neutral.Id, 10)); // arrives tick 30

        match.Advance(17);
        Assert.Equal(match.HumanPlayer, neutral.Owner); // 8 > 5, captured with 3

        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 2)); // arrives tick 37

        match.Advance(13); // elapsed 30: AI's army lands
        Assert.Equal(match.AiPlayer, neutral.Owner); // AI's 10 beats whatever the base grew to, captured

        match.Advance(7); // elapsed 37: human's reinforcement-turned-attack lands
        Assert.Equal(match.AiPlayer, neutral.Owner); // human's 2 is not enough: repelled, AI keeps it
    }

    [Fact]
    public void SameTickArrival_TwoIdenticalArmies_ResolveSequentially()
    {
        var (match, human, _, neutrals) = NewMatch();
        var neutral = neutrals[0];

        // Both armies carry the neutral's own starting garrison. Evaluated independently each would
        // merely tie (5 vs 5); processed one at a time the first tie empties the base and the second
        // then captures it - proving same-tick arrivals apply sequentially, not against one snapshot.
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5));
        match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, neutral.Id, 5));

        match.Advance(17);

        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(5, neutral.GarrisonCount);
    }

    [Fact]
    public void ArrivalTick_SkippedOverByALargeAdvance_StillResolvesExactlyOnce()
    {
        var oneShot = new Match();
        var oneShotHuman = oneShot.Bases.Single(b => b.Owner == oneShot.HumanPlayer);
        var oneShotNeutral = oneShot.Bases.First(b => b.Owner is null);
        oneShot.Execute(new SendArmyCommand(oneShot.HumanPlayer, oneShotHuman.Id, oneShotNeutral.Id, 8));
        oneShot.Advance(100); // arrival was due at tick 17, jumped straight past it in one call

        var stepped = new Match();
        var steppedHuman = stepped.Bases.Single(b => b.Owner == stepped.HumanPlayer);
        var steppedNeutral = stepped.Bases.First(b => b.Owner is null);
        stepped.Execute(new SendArmyCommand(stepped.HumanPlayer, steppedHuman.Id, steppedNeutral.Id, 8));
        stepped.Advance(17); // reach the arrival tick exactly, one tick at a time from here on
        for (var i = 0; i < 83; i++)
        {
            stepped.Advance(1);
        }

        // The army resolved exactly once either way, and continued production afterward is credited
        // identically regardless of how the 100 ticks were split across Advance calls.
        Assert.Equal(oneShot.HumanPlayer, oneShotNeutral.Owner);
        Assert.Empty(oneShot.ArmiesInFlight);
        Assert.Equal(steppedNeutral.Owner, oneShotNeutral.Owner);
        Assert.Equal(steppedNeutral.GarrisonCount, oneShotNeutral.GarrisonCount);
    }

    // --- Determinism ---

    [Fact]
    public void Determinism_SameCommandsAtSameTickCounts_ProduceIdenticalResults_RegardlessOfChunking()
    {
        var oneCall = new Match();
        var oneCallHuman = oneCall.Bases.Single(b => b.Owner == oneCall.HumanPlayer);
        var oneCallNeutral = oneCall.Bases.First(b => b.Owner is null);
        oneCall.Execute(new SendArmyCommand(oneCall.HumanPlayer, oneCallHuman.Id, oneCallNeutral.Id, 6));
        oneCall.Advance(40);

        var chunked = new Match();
        var chunkedHuman = chunked.Bases.Single(b => b.Owner == chunked.HumanPlayer);
        var chunkedNeutral = chunked.Bases.First(b => b.Owner is null);
        chunked.Execute(new SendArmyCommand(chunked.HumanPlayer, chunkedHuman.Id, chunkedNeutral.Id, 6));
        foreach (var chunk in new long[] { 1, 4, 2, 10, 20, 3 })
        {
            chunked.Advance(chunk);
        }

        Assert.Equal(
            oneCall.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)),
            chunked.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount)));
        Assert.Equal(
            oneCall.ArmiesInFlight.Select(a => (a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.ArrivalTick)),
            chunked.ArmiesInFlight.Select(a => (a.SourceBaseId, a.TargetBaseId, a.UnitCount, a.ArrivalTick)));
    }

    private static double Distance(MapPoint a, MapPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
