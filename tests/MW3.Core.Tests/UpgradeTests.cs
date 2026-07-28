namespace MW3.Core.Tests;

public class UpgradeTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    [Fact]
    public void Upgrade_SubtractsTheCostAndRaisesTheLevelByExactlyOne()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var cost = LevelTable.UpgradeCost(LevelTable.MinLevel);

        var outcome = match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id));

        Assert.Equal(UpgradeOutcome.Accepted, outcome);
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
        Assert.Equal(10 - cost, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_NewCapAndPeriodTakeEffectFromThatTick()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        Assert.Equal(LevelTable.GarrisonCap(2), humanBase.GarrisonCap);

        // Level 2 produces every 7 ticks, so 7 ticks from the upgrade yields exactly one unit.
        var atUpgrade = humanBase.GarrisonCount;
        match.Advance(6);
        Assert.Equal(atUpgrade, humanBase.GarrisonCount);
        match.Advance(1);
        Assert.Equal(atUpgrade + 1, humanBase.GarrisonCount);

        match.Advance(10_000);
        Assert.Equal(LevelTable.GarrisonCap(2), humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_CarriesAccumulatedProductionProgress_RatherThanRestartingTheCycle()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(6); // 6 ticks into a 10-tick period
        Assert.Equal(6, humanBase.ProductionProgressTicks);
        var before = humanBase.GarrisonCount;

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(6, humanBase.ProductionProgressTicks); // carried, not reset

        // 6 ticks of progress already meets level 2's 7-tick period after a single further tick.
        match.Advance(1);
        Assert.Equal(before - LevelTable.UpgradeCost(LevelTable.MinLevel) + 1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_ProgressFrozenAtTheCap_ResumesFromThere_WithoutBankingUnits()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(100); // exactly at the level-1 cap of 20
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        match.Advance(500); // frozen: no progress may accumulate at the cap
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(20 - LevelTable.UpgradeCost(LevelTable.MinLevel), humanBase.GarrisonCount);

        // Nothing was banked during those 500 ticks: the first unit is a full level-2 period away.
        var afterUpgrade = humanBase.GarrisonCount;
        match.Advance(LevelTable.ProductionPeriodTicks(2) - 1);
        Assert.Equal(afterUpgrade, humanBase.GarrisonCount);
        match.Advance(1);
        Assert.Equal(afterUpgrade + 1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_AllTheWayDownToZeroGarrison_IsLegal_AndTheBaseKeepsProducing()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(60); // 16 units: exactly the cost of the second upgrade
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(10, humanBase.GarrisonCount);

        match.Advance(42); // level 2 at 7 ticks per unit: back up to 16
        Assert.Equal(16, humanBase.GarrisonCount);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        Assert.Equal(0, humanBase.GarrisonCount);
        Assert.Equal(LevelTable.MaxLevel, humanBase.Level);
        Assert.Equal(match.HumanPlayer, humanBase.Owner); // still owned at zero

        match.Advance(LevelTable.ProductionPeriodTicks(LevelTable.MaxLevel));
        Assert.Equal(1, humanBase.GarrisonCount); // and still producing
    }

    [Fact]
    public void EmptiedByUpgrade_CanBeTakenByASingleUnit()
    {
        // Timed so the AI's single unit lands the tick *after* the second upgrade empties the base,
        // before it has produced a defender: the capital-to-capital flight is 38 ticks, so the AI
        // launches at tick 65 to arrive at 103, and the upgrade that spends the last 16 units
        // happens at tick 102.
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        match.Advance(60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        match.Advance(5); // tick 65
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 1)));
        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(103, army.ArrivalTick);

        match.Advance(37); // tick 102: six level-2 periods since the first upgrade have restored 16
        Assert.Equal(16, humanBase.GarrisonCount);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(0, humanBase.GarrisonCount);

        match.Advance(1); // tick 103: one tick is not a level-3 period, so no defender appears

        Assert.Equal(match.AiPlayer, humanBase.Owner);
        Assert.Equal(1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_UnknownBaseId_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var before = Snapshot(match);

        Assert.Equal(UpgradeOutcome.BaseNotFound, match.Execute(new UpgradeCommand(match.HumanPlayer, 99)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_BaseOwnedByTheOtherPlayer_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var before = Snapshot(match);

        Assert.Equal(
            UpgradeOutcome.BaseNotOwnedByIssuer,
            match.Execute(new UpgradeCommand(match.HumanPlayer, AiBase(match).Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_NeutralBase_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);
        var before = Snapshot(match);

        Assert.Equal(
            UpgradeOutcome.BaseNotOwnedByIssuer,
            match.Execute(new UpgradeCommand(match.HumanPlayer, neutral.Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_GarrisonBelowCost_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        // Spend down to 4, below the cost of 6.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 6)));
        Assert.Equal(4, humanBase.GarrisonCount);
        var before = Snapshot(match);

        Assert.Equal(
            UpgradeOutcome.GarrisonBelowCost,
            match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_AtMaxLevel_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(200);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(LevelTable.MaxLevel, humanBase.Level);

        match.Advance(500);
        var before = Snapshot(match);

        Assert.Equal(
            UpgradeOutcome.AlreadyAtMaxLevel,
            match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_OnceTheMatchIsDecided_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        // Put the AI base within reach of one strike, then take it: with no bases and no armies in
        // flight the AI is eliminated and the outcome is decided.
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!
            .Invoke(aiBase, new object?[] { 1 });

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        match.Advance(200);
        Assert.Equal(MatchOutcome.HumanVictory, match.Outcome);

        var before = Snapshot(match);
        Assert.Equal(
            UpgradeOutcome.MatchAlreadyDecided,
            match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_NullCommand_Throws()
    {
        var match = new Match();

        Assert.Throws<ArgumentNullException>(() => match.Execute((UpgradeCommand)null!));
    }

    [Fact]
    public void Upgrade_NullIssuingPlayer_Throws_RatherThanMatchingANeutralBasesAbsentOwner()
    {
        var match = new Match();
        var neutral = match.Bases.First(b => b.Owner is null);

        // Without an explicit check, `target.Owner != command.IssuingPlayer` is null != null, which
        // is false - so a null issuer would pass the ownership gate on a base nobody owns.
        Assert.Throws<ArgumentException>(() => match.Execute(new UpgradeCommand(null!, neutral.Id)));
        Assert.Throws<ArgumentException>(() => match.Execute(new SendArmyCommand(null!, neutral.Id, match.Bases[0].Id, 1)));

        Assert.Null(neutral.Owner);
        Assert.Equal(LevelTable.MinLevel, neutral.Level);
        Assert.Equal(5, neutral.GarrisonCount);
    }

    [Fact]
    public void MatchRunner_SubmitsUpgrades_ThroughTheSameSinglePath()
    {
        var match = new Match();
        var runner = new MatchRunner(match, new AiBrain(match.AiPlayer));
        var humanBase = HumanBase(match);

        var outcome = runner.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id));

        Assert.Equal(UpgradeOutcome.Accepted, outcome);
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
    }

    private static (int Id, Player? Owner, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();
}
