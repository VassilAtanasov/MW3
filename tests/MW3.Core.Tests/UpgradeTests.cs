namespace MW3.Core.Tests;

public class UpgradeTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
    }

    [Fact]
    public void Upgrade_SubtractsTheCostImmediately_ButOnlyRaisesTheLevelOnCompletion()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var cost = LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel);

        var outcome = match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id));

        Assert.Equal(UpgradeOutcome.Accepted, outcome);
        Assert.Equal(10 - cost, humanBase.GarrisonCount); // paid immediately
        Assert.Equal(LevelTable.MinLevel, humanBase.Level); // benefit delayed - still building (D-30)
        Assert.NotNull(humanBase.Construction);

        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
        Assert.Null(humanBase.Construction);
    }

    [Fact]
    public void Upgrade_NewCapTakesEffectOnlyFromTheCompletionTick()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, LevelTable.MinLevel), humanBase.GarrisonCap); // old cap while building

        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, 2), humanBase.GarrisonCap);

        match.Advance(10_000);
        Assert.Equal(LevelTable.GarrisonCap(BaseType.Producer, 2), humanBase.GarrisonCount);
    }

    [Fact]
    public void Upgrade_ProducesAtItsCurrentLevelTheWholeTimeItIsBuilding()
    {
        // Settled at FR-3c's kickoff: a building under construction keeps working, at its *current*
        // level's period - it does not halt, and it does not jump to the new period early.
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(6); // 6 ticks into the level-1, 60-tick period
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(6, humanBase.ProductionProgressTicks); // carried, not reset by starting the build
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
        var atStart = humanBase.GarrisonCount;

        // The 100-tick build (D-30, FR-3c) keeps producing at the still-current level-1 rate: 6 + 100
        // = 106 available ticks at the 60-tick period produces exactly one unit, carrying a remainder
        // of 46 - which is why the base holds one more unit than it started the build with, not zero.
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
        Assert.Equal(atStart + 1, humanBase.GarrisonCount);
        Assert.Equal(46, humanBase.ProductionProgressTicks);
    }

    [Fact]
    public void Upgrade_CarriedProgressThatAlreadyExceedsTheNewPeriod_RollsOverOnTheVeryNextTick()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);

        // 0 + 100 available ticks at the level-1 period of 60 produces one unit with a remainder of
        // 40 - already past level 2's 30-tick period, so it is not wasted: it rolls into a unit on
        // the very next tick, under the new, shorter period.
        Assert.Equal(40, humanBase.ProductionProgressTicks);
        var atCompletion = humanBase.GarrisonCount;

        match.Advance(1);

        Assert.Equal(atCompletion + 1, humanBase.GarrisonCount);
        Assert.Equal(41 - LevelTable.Village.ProductionPeriodTicks(2), humanBase.ProductionProgressTicks);
    }

    [Fact]
    public void Upgrade_ProgressFrozenAtTheCap_IsNotBanked_ButTheBuildItselfCanStillProduce()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        match.Advance(600); // exactly at the level-1 cap of 20: (20-10) units at 60 ticks/unit
        Assert.Equal(20, humanBase.GarrisonCount);
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        match.Advance(500); // held at the cap: nothing may accumulate here
        Assert.Equal(0, humanBase.ProductionProgressTicks);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(20 - LevelTable.UpgradeCost(BaseType.Producer, LevelTable.MinLevel), humanBase.GarrisonCount);

        // Nothing was banked during the 500 ticks held at the cap - the build starts from zero
        // progress, not from something smuggled through the cap.
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
    }

    [Fact]
    public void Upgrade_ThreeTimesInSequence_ReachesTheUpgradableCeiling_AndKeepsProducingThere()
    {
        // Each upgrade must wait for the previous one's build to finish - only one build at a time
        // (D-30) - with plenty of margin between so affordability is never in doubt.
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(1) + 500);
        Assert.Equal(2, humanBase.Level);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(2) + 500);
        Assert.Equal(3, humanBase.Level);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(3) + 500);

        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);
        Assert.Equal(match.HumanPlayer, humanBase.Owner);
        Assert.True(humanBase.GarrisonCount >= 0);

        var beforeFurtherProduction = humanBase.GarrisonCount;
        match.Advance(LevelTable.Village.ProductionPeriodTicks(LevelTable.MaxUpgradableLevel(BaseType.Producer)));
        Assert.True(humanBase.GarrisonCount > beforeFurtherProduction); // still producing at the ceiling
    }

    [Fact]
    public void EmptiedByPayingForAnUpgrade_CanBeTakenByASmallForce_WhichDiscardsTheBuild()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        SetGarrison(humanBase, 0); // emptied by the upgrade's cost, as if paid at exactly this moment
        Assert.NotNull(humanBase.Construction); // still building - a garrison of zero does not cancel it

        // The 76-tick capital-to-capital flight is inside the 100-tick build, so the base is still
        // under construction (and still producing at level 1, D-30) when this arrives - one tick of
        // level-1 production restores exactly one unit over that span, so two units (not one) are
        // needed to still capture rather than merely tie.
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 2)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.AiPlayer, humanBase.Owner);
        Assert.Null(humanBase.Construction); // the build was discarded with the capture (D-30, FR-3c)
        Assert.Equal(LevelTable.MinLevel, humanBase.Level); // no refund either: still at the level it was building from
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
    public void Upgrade_AlreadyUnderConstruction_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        var before = Snapshot(match);

        Assert.Equal(
            UpgradeOutcome.UnderConstruction,
            match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(before, Snapshot(match));
    }

    [Fact]
    public void Upgrade_AtMaxLevel_IsRejected_LeavingStateUntouched()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        // Three completed upgrades - costs 5, 10, 20 - reach the upgradable ceiling (level 4).
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(1) + 500);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(2) + 500);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(3) + 500);
        Assert.Equal(LevelTable.MaxUpgradableLevel(BaseType.Producer), humanBase.Level);

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
        SetGarrison(aiBase, 1);

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

        runner.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
    }

    private static (int Id, Player? Owner, int Garrison, int Level, long Progress)[] Snapshot(Match match) =>
        match.Bases.Select(b => (b.Id, b.Owner, b.GarrisonCount, b.Level, b.ProductionProgressTicks)).ToArray();
}
