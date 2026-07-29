namespace MW3.Core.Tests;

/// <summary>
/// FR-3c (D-30) coverage that is neither an <see cref="UpgradeCommand"/>/<see cref="ConvertCommand"/>
/// rejection reason (see <see cref="UpgradeTests"/>/<see cref="ConvertTests"/>) nor the recapture
/// grace (see <see cref="RecaptureGraceTests"/>): completion ordering within a tick, and what a
/// capture does to a build in progress.
/// </summary>
public class ConstructionTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    /// <summary>
    /// Construction completes before that tick's arrivals resolve (D-30): a base finishing an
    /// upgrade on the exact tick it is attacked defends at its new, higher level - not its old one.
    /// </summary>
    [Fact]
    public void ABaseFinishingAnUpgrade_OnTheTickItIsAttacked_DefendsAtItsNewLevel()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        var completionTick = LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel);

        // The capital-to-capital flight is a fixed 76 ticks; launching the attack once the match has
        // advanced to (completionTick - 76) makes it land on exactly the completion tick - the one
        // tick that proves ordering rather than merely "eventually, before or after".
        match.Advance(completionTick - 76);
        SetGarrison(humanBase, 20); // a healthy garrison to defend with once level 2 (110%) applies
        SetGarrison(aiBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 21)));
        var army = match.ArmiesInFlight.Single();
        Assert.Equal(completionTick, army.ArrivalTick); // confirms the scripted timing landed exactly

        // Wu*a = 21*100 = 2100. At level 1 (100% defence) with 20 defenders, Du*d = 2000 - captured.
        // At level 2 (110%), Du*d = 2200 - held. Only the ordering (construction before arrivals)
        // decides which of the two this is.
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, humanBase.Owner); // held - the level-2 defence already applied
        Assert.Equal(LevelTable.MinLevel + 1, humanBase.Level);
    }

    [Fact]
    public void CapturedWhileUnderConstruction_DiscardsTheBuild_WithNoRefund()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        var spentOnUpgrade = 10 - humanBase.GarrisonCount;
        Assert.True(spentOnUpgrade > 0);
        Assert.NotNull(humanBase.Construction);

        SetGarrison(humanBase, 1);
        SetGarrison(aiBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 30)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, humanBase.Owner);
        Assert.Null(humanBase.Construction); // discarded, not completed for the new owner
        Assert.Equal(LevelTable.MinLevel, humanBase.Level); // no refund: still the level it was building from
    }

    [Fact]
    public void CapturedWhileUnderConstruction_NeverCompletesForTheNewOwner_EvenAfterTheOriginalCompletionTick()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));
        var completionTick = match.ElapsedTicks + LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel);

        SetGarrison(humanBase, 1);
        SetGarrison(aiBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, humanBase.Id, 30)));
        var army = match.ArmiesInFlight.Single();
        Assert.True(army.ArrivalTick < completionTick); // captured before the original build would have finished
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
        Assert.Equal(match.AiPlayer, humanBase.Owner);

        // Advance well past what would have been the original completion tick: nothing completes for
        // the AI, because the build was discarded outright, not merely reassigned.
        match.Advance(completionTick - match.ElapsedTicks + 500);

        Assert.Null(humanBase.Construction);
        Assert.Equal(LevelTable.MinLevel, humanBase.Level);
    }

    [Fact]
    public void NoConstructionCompletes_OnceTheMatchOutcomeIsDecided()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);

        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, humanBase.Id)));

        // Eliminate the AI immediately: no bases and no armies in flight decides the match this tick.
        SetGarrison(aiBase, 1);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 5)));
        var army = match.ArmiesInFlight.Single(a => a.TargetBaseId == aiBase.Id);
        match.Advance(army.ArrivalTick - match.ElapsedTicks);
        Assert.NotEqual(MatchOutcome.InProgress, match.Outcome);

        var levelAtDecision = humanBase.Level;
        Assert.Equal(LevelTable.MinLevel, levelAtDecision); // still building - the outcome was decided first

        match.Advance(1000); // well past the build's completion tick

        Assert.Equal(levelAtDecision, humanBase.Level); // frozen: the simulation stopped advancing at all
    }
}
