namespace MW3.Core.Tests;

/// <summary>
/// FR-1's four accrual sites: capture, attacking-unit deaths (arrival combat and tower fire), and
/// completed upgrades (D-41, docs/morale/REQUIREMENTS.md FR-1). Per-event assertions over end-state
/// totals wherever the arithmetic allows it (docs/morale/ARCHITECTURE.md §5), since the gain/loss
/// tables are large enough that a wrong row hides easily behind a right-looking total.
/// </summary>
public class MoraleAccrualTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetOwnerBeforeLastChange(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.OwnerBeforeLastChange))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetLastOwnerChangeTick(Base b, long? tick) =>
        typeof(Base).GetProperty(nameof(Base.LastOwnerChangeTick))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { tick });

    private static void SetMoralePoints(MoraleState state, int points) =>
        typeof(MoraleState).GetProperty(nameof(MoraleState.Points))!.GetSetMethod(nonPublic: true)!.Invoke(state, new object?[] { points });

    private static void ConvertToTower(Match match, Player owner, Base b)
    {
        SetGarrison(b, LevelTable.ConversionCost + 20);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(owner, b.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Tower, b.Type);
    }

    private static void AdvanceToNextArrival(Match match)
    {
        var army = match.ArmiesInFlight.OrderBy(a => a.ArrivalTick).First();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
    }

    // ---- Captures ----

    /// <summary>
    /// The worked example recorded at kickoff (docs/morale/REQUIREMENTS.md FR-1): the human's opening
    /// 10-unit send splits into waves of 8 and 2 (FR-3); wave 1 (8 units, attack index 100) beats the
    /// level-1 neutral's 5-unit garrison (defence index 100), leaving 3 survivors - so 5 units died
    /// attacking. +40 for the capture against -50 for the 5 deaths nets a -10 swing: capturing a
    /// neutral is morale-negative here, which is MW2's design (morale rewards not-losing), not a bug.
    /// Started with headroom, like the sibling tests below - starting at 0 would clamp the swing at
    /// the D-38 floor before it could show as a full -10 move, which would test the floor instead of
    /// the swing.
    /// </summary>
    [Fact]
    public void Capture_Neutral_Level1_Village_NetsNegativeTen_TheWorkedExample()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetMoralePoints(match.HumanMorale, 100); // headroom so the net -10 swing does not clamp at the floor

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        match.Advance(1000); // past every wave's arrival, including the reinforcing second wave

        Assert.Equal(match.HumanPlayer, neutral.Owner);
        var expectedGain = MoraleTable.CaptureGain(BaseType.Producer, LevelTable.MinLevel, wasOpponentOwned: false);
        Assert.Equal(40, expectedGain);
        Assert.Equal(100 - 10, match.HumanMorale.Points); // net -10 swing: +40 capture, -50 for the 5 deaths
        Assert.Equal(0, match.AiMorale.Points); // neutral scores nothing for nobody but the capturer
    }

    [Fact]
    public void Capture_OpponentLevel1Village_AwardsCapturerAndChargesPreviousOwner_SeparatelyFromUnitDeaths()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        SetMoralePoints(match.AiMorale, 500); // headroom so the loss below does not clamp at the floor
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        // The human-to-aiBase flight is 76 ticks; a level-1 producer's 60-tick production period
        // fires once en route, growing the garrison from 1 to 2 before combat. attacker index 100,
        // defender index 100 (level 1): remaining = (5*100 - 2*100)/100 = 3, 2 units died attacking.
        Assert.Equal(100 - 20, match.HumanMorale.Points); // +100 capture (opponent, level 1 village) - 20 (2 died attacking)
        Assert.Equal(500 + 20 - 50, match.AiMorale.Points); // +20 (destroyed two attacking units) - 50 (lost a level-1 village)
    }

    /// <summary>
    /// A retake inside the recapture grace (FR-1, LevelTable.RecaptureGraceTicks) still awards
    /// capture and combat morale normally - the grace skips demotion only. The target is rigged by
    /// reflection to already be AI-owned with the human as its previous owner (the same style
    /// CaptureDemotionTests uses for states ordinary play cannot reach quickly); only the retake
    /// itself is a real, resolved event.
    /// </summary>
    [Fact]
    public void Retake_WithinGrace_StillAwardsMoraleNormally_OnlyDemotionIsSkipped()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var target = match.Bases.First(b => b.Owner is null);

        SetOwner(target, match.AiPlayer);
        SetLevel(target, 2);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.HumanPlayer);
        SetMoralePoints(match.AiMorale, 200); // headroom so the loss below does not clamp at the floor
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, target.Id, 5)));
        var army = match.ArmiesInFlight.Single();
        SetLastOwnerChangeTick(target, army.ArrivalTick - 15); // inside the 20-tick grace of the retake

        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, target.Owner);
        Assert.Equal(2, target.Level); // demotion skipped - this is a retake within grace

        // The human-to-target flight is 34 ticks; a level-2 producer's 30-tick production period
        // fires once en route, growing the garrison from 1 to 2 before combat. attacker index 100,
        // defender index 110 (level 2 village): remaining = (5*100 - 2*110)/110 = 2, 3 died attacking.
        Assert.Equal(250 - 30, match.HumanMorale.Points); // +250 capture (opponent, level 2 village) - 30 (3 died attacking)
        Assert.Equal(200 + 30 - 120, match.AiMorale.Points); // +30 (destroyed three attacking units) - 120 (lost a level-2 village)
    }

    [Fact]
    public void ReinforcingYourOwnBase_ScoresNoMorale()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 10)));
        match.Advance(1000);
        Assert.Equal(match.HumanPlayer, neutral.Owner);

        var afterCapture = match.HumanMorale.Points;
        SetGarrison(humanBase, 5);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, neutral.Id, 3)));
        match.Advance(1000);

        Assert.Equal(afterCapture, match.HumanMorale.Points); // reinforcement is not combat
    }

    // ---- Attacking-unit deaths (D-41) ----

    [Fact]
    public void FailedAttack_AllAttackingUnitsDie_LossToAttacker_GainToDefender()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        SetMoralePoints(match.HumanMorale, 200); // headroom so the loss below does not clamp at the floor
        SetGarrison(aiBase, 100);
        SetGarrison(humanBase, 5);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.AiPlayer, aiBase.Owner); // held - not captured
        Assert.Equal(200 - 50, match.HumanMorale.Points); // all 5 attacking units died: -10 each
        Assert.Equal(0 + 50, match.AiMorale.Points); // defender destroyed 5 attacking units: +10 each
    }

    [Fact]
    public void SuccessfulCapture_OnlySurvivingLossCounts_NotTheWholeWave()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        SetMoralePoints(match.AiMorale, 500);
        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        AdvanceToNextArrival(match);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        // The human-to-aiBase flight is 76 ticks; a level-1 producer's 60-tick production period
        // fires once en route, growing the garrison from 1 to 2 before combat. remaining =
        // (5*100 - 2*100)/100 = 3 survivors -> 2 died attacking, not all 5.
        Assert.Equal(3, aiBase.GarrisonCount);
        var deathSwing = 2 * MoraleTable.AttackingUnitDestroyedGain;
        Assert.Equal(MoraleTable.CaptureGain(BaseType.Producer, LevelTable.MinLevel, wasOpponentOwned: true) - deathSwing, match.HumanMorale.Points);
    }

    // ---- Tower fire (D-41) ----

    [Fact]
    public void TowerFire_DestroyingAnArmyOutright_AwardsShooterAndPenalizesArmyOwner_PerShot()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);
        SetMoralePoints(match.AiMorale, 200); // headroom so the loss below does not clamp at the floor

        SetGarrison(aiBase, 4);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 4)));
        var army = match.ArmiesInFlight.Single();

        match.Advance(army.ArrivalTick - match.ElapsedTicks + 5); // well past arrival - the tower gets every shot

        Assert.Empty(match.ArmiesInFlight); // destroyed outright, removed from ArmiesInFlight
        Assert.Equal(4 * MoraleTable.AttackingUnitDestroyedGain, match.HumanMorale.Points); // 4 shots to destroy 4 units
        Assert.Equal(200 - (4 * MoraleTable.AttackingUnitDiedLoss), match.AiMorale.Points);
    }

    // ---- Upgrades ----

    [Fact]
    public void CompletedVillageUpgrade_AwardsOwner_ByResultingLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        Assert.Equal(0, match.HumanMorale.Points); // acceptance alone awards nothing (FR-1)

        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Equal(2, humanBase.Level);
        Assert.Equal(100, match.HumanMorale.Points); // village to level 2 = +100
    }

    [Fact]
    public void CompletedTowerUpgrade_AwardsOwner_ByResultingLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        ConvertToTower(match, match.HumanPlayer, humanBase);
        Assert.Equal(0, match.HumanMorale.Points); // a completed conversion awards nothing

        SetGarrison(humanBase, LevelTable.Tower.UpgradeCost(LevelTable.MinLevel));
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        match.Advance(LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));

        Assert.Equal(2, humanBase.Level);
        Assert.Equal(200, match.HumanMorale.Points); // tower to level 2 = +200
    }

    [Fact]
    public void CapturedWhileUnderConstruction_AwardsNoUpgradeMoraleToAnyone()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        var completionTick = LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel);

        SetGarrison(humanBase, 1);
        SetGarrison(aiBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 30)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, humanBase.Owner);
        Assert.Null(humanBase.Construction); // discarded, not completed for the new owner

        var humanPointsAfterCapture = match.HumanMorale.Points;
        var aiPointsAfterCapture = match.AiMorale.Points;

        match.Advance(completionTick + 100); // well past the original, now-discarded completion tick

        Assert.Equal(humanPointsAfterCapture, match.HumanMorale.Points); // no upgrade morale ever landed
        Assert.Equal(aiPointsAfterCapture, match.AiMorale.Points);
    }

    [Fact]
    public void CompletedConversion_AwardsNothing()
    {
        var match = new Match();
        var humanBase = HumanBase(match);

        ConvertToTower(match, match.HumanPlayer, humanBase);

        Assert.Equal(0, match.HumanMorale.Points);
    }
}
