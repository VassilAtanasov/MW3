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
        var cost = LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel);

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

        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, 2), humanBase.GarrisonCap);

        // Level 2 produces every 30 ticks, so 30 ticks from the upgrade yields exactly one unit.
        var atUpgrade = humanBase.GarrisonCount;
        match.Advance(29);
        Assert.Equal(atUpgrade, humanBase.GarrisonCount);
        match.Advance(1);
        Assert.Equal(atUpgrade + 1, humanBase.GarrisonCount);

        match.Advance(10_000);
        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, 2), humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_CarriesAccumulatedProductionProgress_RatherThanRestartingTheCycle()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(6); // 6 ticks into a 60-tick period
        Assert.Equal(6, humanBase.ProductionProgressTicks);
        var before = humanBase.GarrisonCount;

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(6, humanBase.ProductionProgressTicks); // carried, not reset

        // The 6 ticks of carried progress plus 24 further ticks meets level 2's 30-tick period.
        match.Advance(24);
        Assert.Equal(before - LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel) + 1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_ProgressFrozenAtTheCap_ResumesFromThere_WithoutBankingUnits()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(600); // exactly at the level-1 cap of 20: (20-10) units at 60 ticks/unit
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        match.Advance(500); // frozen: no progress may accumulate at the cap
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(20 - LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), humanBase.GarrisonCount);

        // Nothing was banked during those 500 ticks: the first unit is a full level-2 period away.
        var afterUpgrade = humanBase.GarrisonCount;
        match.Advance(LevelTable.Village.ProductionPeriodTicks(2) - 1);
        Assert.Equal(afterUpgrade, humanBase.GarrisonCount);
        match.Advance(1);
        Assert.Equal(afterUpgrade + 1, humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_AllTheWayDownToZeroGarrison_IsLegal_AndTheBaseKeepsProducing()
    {
        // Three upgrades - costs 5, 10, 20 - reach the upgradable ceiling (level 4), each timed so
        // production exactly restores what the previous upgrade spent before the next one is paid,
        // landing the third upgrade on exactly zero.
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(300); // level 1 at 60 ticks/unit: 10 + 5 = 15
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(10, humanBase.GarrisonCount); // 15 - 5

        match.Advance(300); // level 2 at 30 ticks/unit: 10 + 10 = 20
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(10, humanBase.GarrisonCount); // 20 - 10

        match.Advance(200); // level 3 at 20 ticks/unit: 10 + 10 = 20
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        Assert.Equal(0, humanBase.GarrisonCount); // 20 - 20
        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);
        Assert.Equal(match.HumanPlayer, humanBase.Owner); // still owned at zero

        match.Advance(LevelTable.Village.ProductionPeriodTicks(LevelTable.MaxUpgradableLevel(BaseType.Producer)));
        Assert.Equal(1, humanBase.GarrisonCount); // and still producing
    }

    [Fact]
    public void EmptiedByUpgrade_CanBeTakenByASingleUnit()
    {
        // Timed so the AI's single unit lands the tick *after* the second upgrade empties the base,
        // before it has produced a defender. The capital-to-capital flight is 76 ticks: the first
        // upgrade (cost 5) lands at tick 60, leaving garrison 6 at level 2 (period 30); the AI
        // launches its single unit at tick 105 to arrive at 181; by tick 180 three more level-2
        // periods have restored the garrison to exactly the second upgrade's cost of 10, which is
        // spent in full at that tick, leaving nothing to defend the arrival one tick later.
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        match.Advance(60); // tick 60: level 1 at 60 ticks/unit produces exactly one: 10 + 1 = 11
        Assert.Equal(11, humanBase.GarrisonCount);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(6, humanBase.GarrisonCount); // 11 - 5

        match.Advance(45); // tick 105
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 1)));
        var army = Assert.Single(match.ArmiesInFlight);
        Assert.Equal(181, army.ArrivalTick);

        match.Advance(75); // tick 180: four level-2 periods since the first upgrade have restored 4 units
        Assert.Equal(10, humanBase.GarrisonCount);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(0, humanBase.GarrisonCount);

        match.Advance(1); // tick 181: one tick is nowhere near a level-3 period, so no defender appears

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

        // Spend down to 4, below the cost of 5.
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

        // Three upgrades - costs 5, 10, 20 - reach the upgradable ceiling (level 4).
        match.Advance(60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(200);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(400);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);

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
