namespace MW3.Core.Tests;

/// <summary>
/// Phase 6 FR-1: <see cref="BaseType.Forge"/> exists, has exactly one tier, produces no units, never
/// fires, and survives capture as a forge. See issue #82's acceptance criteria.
/// </summary>
public class ForgeTypeTests
{
    private static Base HumanBase(Match match) => match.Bases.Single(b => b.Owner == match.HumanPlayer);

    private static Base AiBase(Match match) => match.Bases.Single(b => b.Owner == match.AiPlayer);

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static void SetOwner(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static void SetLastOwnerChangeTick(Base b, long? tick) =>
        typeof(Base).GetProperty(nameof(Base.LastOwnerChangeTick))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { tick });

    private static void SetOwnerBeforeLastChange(Base b, Player? owner) =>
        typeof(Base).GetProperty(nameof(Base.OwnerBeforeLastChange))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { owner });

    private static Match ConvertHumanBaseToForge(out Base humanBase)
    {
        var match = new Match();
        humanBase = HumanBase(match);
        SetGarrison(humanBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, humanBase.Id, BaseType.Forge)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Forge, humanBase.Type);
        return match;
    }

    [Fact]
    public void Forge_NeverProduces_GarrisonUnchangedAfter1000Ticks_WhileAProducerBesideItGrows()
    {
        var match = ConvertHumanBaseToForge(out var forge);
        var producer = AiBase(match); // still an ordinary producer - untouched by the human's conversion
        var forgeGarrison = forge.GarrisonCount;
        var producerGarrison = producer.GarrisonCount;

        match.Advance(1000);

        Assert.Equal(forgeGarrison, forge.GarrisonCount);
        Assert.True(producer.GarrisonCount > producerGarrison);
    }

    [Fact]
    public void Forge_NeverFires_EnemyArmyWithinALevelOneTowersRangeLosesNoUnits()
    {
        var match = new Match();
        var aiBase = AiBase(match);
        SetGarrison(aiBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Forge)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Forge, aiBase.Type);

        var humanBase = HumanBase(match);
        SetGarrison(humanBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 8)));
        var army = match.ArmiesInFlight.Single();
        var unitsBeforeArrival = army.UnitCount;
        match.Advance(army.ArrivalTick - match.ElapsedTicks - 1);

        Assert.Equal(unitsBeforeArrival, army.UnitCount);
    }

    [Fact]
    public void Forge_HasExactlyOneLevel_MaxLevelAndMaxUpgradableLevelBothEqualMinLevel()
    {
        Assert.Equal(LevelTable.MinLevel, LevelTable.MaxLevel(BaseType.Forge));
        Assert.Equal(LevelTable.MinLevel, LevelTable.MaxUpgradableLevel(BaseType.Forge));
    }

    [Fact]
    public void Forge_UpgradeCommand_ReturnsAlreadyAtMaxLevel_RatherThanThrowing()
    {
        var match = ConvertHumanBaseToForge(out var forge);

        var outcome = match.Execute(new UpgradeCommand(match.HumanPlayer, forge.Id));

        Assert.Equal(UpgradeOutcome.AlreadyAtMaxLevel, outcome);
    }

    [Fact]
    public void Forge_GarrisonCap_IsNull_LikeATower()
    {
        Assert.Null(LevelTable.GarrisonCap(BaseType.Forge, LevelTable.MinLevel));
    }

    [Fact]
    public void Forge_Base_GarrisonCap_IsEmpty()
    {
        var match = ConvertHumanBaseToForge(out var forge);

        Assert.Null(forge.GarrisonCap);
    }

    [Fact]
    public void Forge_DefencePercentage_IsOneHundred_LikeALevelOneVillage()
    {
        Assert.Equal(100, LevelTable.DefencePercentage(BaseType.Forge, LevelTable.MinLevel));
        Assert.Equal(100, LevelTable.DefencePercentage(BaseType.Producer, LevelTable.MinLevel));
    }

    [Fact]
    public void Forge_UpgradeCost_ThrowsForEveryLevel_NamingItsLackOfAnUpgradePath()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => LevelTable.UpgradeCost(BaseType.Forge, LevelTable.MinLevel));
        Assert.Contains("upgrade path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forge_RingThicknessFractionOfRadius_ReturnsAValue()
    {
        Assert.True(LevelTable.RingThicknessFractionOfRadius(BaseType.Forge, LevelTable.MinLevel) > 0);
    }

    [Fact]
    public void Capture_OfAForge_LeavesItAForgeAtLevelOne_NeitherRevertedNorDestroyed()
    {
        var match = new Match();
        var humanBase = HumanBase(match);
        var aiBase = AiBase(match);
        SetGarrison(aiBase, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.AiPlayer, aiBase.Id, BaseType.Forge)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        Assert.Equal(BaseType.Forge, aiBase.Type);

        SetGarrison(aiBase, 1);
        SetGarrison(humanBase, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, humanBase.Id, aiBase.Id, 10)));
        var army = match.ArmiesInFlight.Single();
        match.Advance(army.ArrivalTick - match.ElapsedTicks);

        Assert.Equal(match.HumanPlayer, aiBase.Owner);
        Assert.Equal(BaseType.Forge, aiBase.Type);
        Assert.Equal(LevelTable.MinLevel, aiBase.Level);
    }

    /// <summary>
    /// Rigged directly by reflection, the same style <see cref="RecaptureGraceTests"/> uses: a
    /// real send's arrival tick sets the grace window's boundary exactly, on a base pre-set to be a
    /// forge that just changed hands. The grace keys on ownership, not on level (a forge has none to
    /// demote in the first place), so a true retake within the window is expected to succeed exactly
    /// as it would for a producer or a tower.
    /// </summary>
    [Fact]
    public void Capture_OfAForge_RecaptureGrace_AppliesUnchanged_KeyingOnOwnershipNotLevel()
    {
        var match = new Match();
        var target = match.Bases.First(b => b.Owner is null);
        var aiBase = AiBase(match);

        SetOwner(target, match.HumanPlayer);
        SetGarrison(target, 1);
        SetOwnerBeforeLastChange(target, match.AiPlayer); // the AI held it immediately before the human

        // Rig the target as a forge without going through a real conversion build - the recapture
        // grace test cares only about ownership timing, not how the base became a forge.
        typeof(Base).GetProperty(nameof(Base.Type))!.GetSetMethod(nonPublic: true)!.Invoke(target, new object?[] { BaseType.Forge });

        SetGarrison(aiBase, 40);

        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.AiPlayer, aiBase.Id, target.Id, 10)));
        var arrivalTick = match.ArmiesInFlight.Single().ArrivalTick;
        SetLastOwnerChangeTick(target, arrivalTick - 5); // well inside the 20-tick window

        match.Advance(arrivalTick - match.ElapsedTicks);

        Assert.Equal(match.AiPlayer, target.Owner);
        Assert.Equal(BaseType.Forge, target.Type); // still a forge - capture never reverts the type
        Assert.Equal(LevelTable.MinLevel, target.Level); // no demotion: a true retake
    }

    [Fact]
    public void ProductionScan_TreatsForgeLikeTower_NeitherProduces()
    {
        var match = new Match();
        var human = HumanBase(match);
        SetGarrison(human, 40);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Forge)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);

        Assert.Equal(0, human.ProductionProgressTicks);
        match.Advance(500);
        Assert.Equal(0, human.ProductionProgressTicks);
    }
}
