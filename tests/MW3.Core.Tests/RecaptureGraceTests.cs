namespace MW3.Core.Tests;

/// <summary>
/// The recapture grace (D-30, FR-3c, <c>MW2-RULES.md</c> §2.5): a capture that retakes a base from
/// the player who held it immediately before its last owner change, within
/// <see cref="LevelTable.RecaptureGraceTicks"/> of that change, skips the usual one-level demotion.
/// States are rigged directly by reflection - the same style <see cref="CaptureDemotionTests"/> uses
/// - because hitting an exact tick offset from a real send's arrival, on a fixed map whose shortest
/// inter-base distance is already 30 ticks, is not reachable through ordinary play inside a 20-tick
/// window. Each test reads back the capturing army's own real <see cref="Army.ArrivalTick"/> and uses
/// it to set the rigged <see cref="Base.LastOwnerChangeTick"/>, so the grace boundary itself is
/// exercised exactly, not approximated.
/// </summary>
public class RecaptureGraceTests
{
    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetLevel(Base b, int level) =>
        typeof(Base).GetProperty(nameof(Base.Level))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { level });

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetLastOwnerChangeTick(Base b, long? tick) =>
        typeof(Base).GetProperty(nameof(Base.LastOwnerChangeTick))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { tick });

    private static void SetOwnerBeforeLastChange(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.OwnerBeforeLastChange))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    [Fact]
    public void Recapture_ByThePreviousOwner_WellWithinGrace_SkipsTheDemotion()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.AiPlayer); // the AI held it immediately before the human
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - 5); // well inside the 20-tick window

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(3, target.Level); // no demotion: a true retake
    }

    [Fact]
    public void Recapture_ByThePreviousOwner_ExactlyAtTheTwentyTickBoundary_StillSkipsTheDemotion()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.AiPlayer);
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - LevelTable.RecaptureGraceTicks); // inclusive boundary

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(3, target.Level); // inclusive at exactly the grace window
    }

    [Fact]
    public void Recapture_ByThePreviousOwner_OneTickPastTheBoundary_DemotesNormally()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.AiPlayer);
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - LevelTable.RecaptureGraceTicks - 1); // one tick outside

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(2, target.Level); // outside the window: the usual one-level demotion applies
    }

    [Fact]
    public void Capture_ByAnyoneOtherThanThePreviousOwner_WithinTheWindow_DemotesNormally()
    {
        // A player who did not hold the base immediately before is not "retaking" it, however soon
        // they capture it after the fact - the window alone is not the rule.
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, null); // some other previous state - not the AI
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick); // zero ticks since the change - as close as possible

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(2, target.Level); // demoted: the AI was never the base's previous owner
    }

    /// <summary>
    /// The specific counterexample the acceptance criteria calls out: neutral -&gt; human -&gt; AI
    /// within the window does not grant the AI the grace, because the AI is not the owner the base
    /// had immediately before the human took it (that owner was neutral - nobody).
    /// </summary>
    [Fact]
    public void NeutralToHumanToAi_WithinTheWindow_DoesNotGrantTheAiTheGrace()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, null); // the base was neutral immediately before the human took it
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - 1); // one tick since the human took it - deep inside the window

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(2, target.Level); // demoted: a loose "any recapture within the window" reading would wrongly skip this
    }

    [Fact]
    public void ABaseThatHasNeverChangedOwner_HasNoGrace()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);
        SetLevel(target, 3);
        SetGarrison(target, 1);
        // LastOwnerChangeTick left null - the base has never changed owner (taking a neutral at
        // match start is the common case this must not disturb).
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(2, target.Level);
    }

    [Fact]
    public void Recapture_WithinGrace_SkipsOnlyTheDemotion_NotAnyOtherEffect()
    {
        // The grace suppresses the level drop and nothing else: it does not restore a level already
        // lost to an earlier, unrelated capture.
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetLevel(target, 2); // already demoted once by an earlier, unrelated capture
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.AiPlayer);
        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - 5);

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(2, target.Level); // held at 2, not restored to 3 and not dropped to 1
    }
}
