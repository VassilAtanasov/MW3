namespace MW3.Core.Tests;

/// <summary>
/// The clamp (D-38) exercised at the <see cref="Match"/> level, through repeated real events rather
/// than reflection, so the assertions are about the actual write path (<c>Match.AwardMorale</c>)
/// and not merely about <see cref="MoraleTable.ClampPoints"/> in isolation (already covered by
/// <see cref="MoraleTableTests"/>).
/// </summary>
public class MoraleStateAndClampTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void NewMatch_BothPlayersStartAtZeroMoraleAndLevelZero()
    {
        var match = new Match();

        Assert.Equal(0, match.HumanMorale.Points);
        Assert.Equal(0, match.HumanMorale.Level);
        Assert.Null(match.HumanMorale.LastSendTick);

        Assert.Equal(0, match.AiMorale.Points);
        Assert.Equal(0, match.AiMorale.Level);
        Assert.Null(match.AiMorale.LastSendTick);
    }

    [Fact]
    public void ALoss_BelowZero_LandsAtExactlyZero()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        // A failed attack: the whole wave dies, costing the human 10 points/unit far beyond their
        // starting zero - the loss must clamp at the floor, not go negative.
        SetGarrison(aiBase, 1000);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(0, match.HumanMorale.Points);
    }

    [Fact]
    public void AGain_AtTheCeiling_LeavesTheValueUnchanged()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(humanBase, MoraleTable.PointCeiling); // arbitrarily large morale-granting garrison to award from repeatedly
        SetHumanMoralePoints(match, MoraleTable.PointCeiling);

        SetGarrison(aiBase, 1);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner); // captured: a gain was actually applied
        Assert.Equal(MoraleTable.PointCeiling, match.HumanMorale.Points); // still clamped at the ceiling
    }

    [Fact]
    public void AGain_AboveTheCeiling_LandsAtExactlyTheCeiling()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetHumanMoralePoints(match, MoraleTable.PointCeiling - 5);

        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(MoraleTable.PointCeiling, match.HumanMorale.Points);
    }

    [Fact]
    public void AnAcceptedSend_RecordsItsSubmissionTickAsLastSendTick()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        match.Advance(37);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 5)));

        Assert.Equal(37, match.HumanMorale.LastSendTick);
        Assert.Null(match.AiMorale.LastSendTick);
    }

    [Fact]
    public void ARejectedSend_LeavesLastSendTickUntouched()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        match.Advance(10);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        Assert.Equal(10, match.HumanMorale.LastSendTick);

        match.Advance(50);
        // Rejected: source not owned by issuer.
        Assert.Equal(SendArmyOutcome.SourceNotOwnedByIssuer, match.Execute(new SendArmyCommand(match.AiPlayer, humanBase.Id, aiBase.Id, 1)));

        Assert.Equal(10, match.HumanMorale.LastSendTick); // unchanged by the rejected command
    }

    private static void SetHumanMoralePoints(Match match, int points) =>
        typeof(MoraleState).GetProperty(nameof(MoraleState.Points))!.GetSetMethod(nonPublic: true)!
            .Invoke(match.HumanMorale, new object?[] { points });

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
    }
}
