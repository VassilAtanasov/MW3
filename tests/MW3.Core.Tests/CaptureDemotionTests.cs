namespace MW3.Core.Tests;

/// <summary>
/// A capture keeps the base but burns one level of the previous owner's investment (D-23). Levels
/// are set up here by playing real upgrades where the garrison allows it, and by reflection where a
/// level-3 base with a specific garrison is needed - the same style of direct construction
/// <see cref="MatchOutcomeTests"/> already uses for states ordinary play cannot reach quickly.
/// </summary>
public class CaptureDemotionTests
{
    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    [Fact]
    public void CapturedBase_DropsExactlyOneLevel()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetLevel(aiBase, 3);
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 30)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(2, aiBase.Level);
    }

    [Fact]
    public void CapturedBase_AtTheMinimumLevel_StaysThere_RatherThanGoingBelowIt()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Equal(LevelTable.MinLevel, neutral.Level);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, neutral.Owner);
        Assert.Equal(LevelTable.MinLevel, neutral.Level);
    }

    [Fact]
    public void CapturedBase_LosesTheProgressItsPreviousOwnerAccumulated()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        match.Advance(7); // the AI base is now 7 ticks into its period
        Assert.Equal(7, aiBase.ProductionProgressTicks);

        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 30)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(0, aiBase.ProductionProgressTicks);
    }

    [Fact]
    public void ReinforcingYourOwnBase_NeverChangesItsLevel()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        SetLevel(neutral, 3);
        match.Advance(300);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 5)));
        AdvanceToNextArrival(match);

        Assert.Equal(3, neutral.Level); // reinforcement is not a capture
    }

    [Fact]
    public void RepelledAttack_DoesNotChangeTheDefendersLevel()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetLevel(aiBase, 2);
        SetGarrison(aiBase, 30);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.AiPlayer, aiBase.Owner);
        Assert.Equal(2, aiBase.Level);
    }

    [Fact]
    public void Demotion_MayLeaveTheGarrisonAboveTheNewLowerCap_WhichIsLegalAndMerelyBlocksProduction()
    {
        var match = new Match();
        var humanBase = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var aiBase = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Give the AI a second base first, so capturing aiBase below does not eliminate it and
        // freeze the rest of the column's pending waves (D-35's own rule: once the outcome is
        // decided, no pending wave ever launches).
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, neutral.Id, 8)));
        AdvanceToNextArrival(match);
        Assert.Equal(match.AiPlayer, neutral.Owner);

        // A level-3 base (cap 50, defence 120% - D-29) captured by a large enough army leaves the
        // attacker holding more than level 2's cap of 40 - so the demotion lands the base above its
        // own new ceiling. 60 units splits into a column of waves (FR-3): the first wave captures
        // and demotes the base, and every later wave in the same send then reinforces the new owner
        // directly rather than fighting it, so the column as a whole still leaves far more than 40
        // behind by the time every wave has landed.
        SetLevel(aiBase, 3);
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 100);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 60)));

        var waveCount = SendWaveCalculator.WaveCount(60);
        var firstWave = match.ArmiesInFlight.OrderBy(a => a.WaveIndex).First();
        var lastWaveArrival = firstWave.ArrivalTick + SendWaveCalculator.LaunchTickOffset(waveCount);
        match.Advance(lastWaveArrival - match.ElapsedTicks + 1); // past every wave's arrival

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(2, aiBase.Level);
        Assert.True(aiBase.GarrisonCount > aiBase.GarrisonCap);

        var aboveCap = aiBase.GarrisonCount;
        match.Advance(1000);
        Assert.Equal(aboveCap, aiBase.GarrisonCount); // nothing destroyed, nothing produced
    }

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
    }
}
