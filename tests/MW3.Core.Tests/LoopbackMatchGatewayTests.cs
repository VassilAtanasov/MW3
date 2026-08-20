namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-3: the in-process implementation of the seam. The claims that matter here are not
/// about the rules - the rules are untouched, and the integration tests below assert exactly that -
/// but about the pipeline: that what the gateway exposes is reached by diffing and applying (D-61),
/// that it diffs once per frame rather than once per tick, and that a send resolves its unit count
/// on this side of the seam from a <see cref="SendStrength"/> (D-76).
/// </summary>
public class LoopbackMatchGatewayTests
{
    private const long _tick = Match.TickDurationMilliseconds;

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    /// <summary>The <see cref="Match"/> a gateway owns. Private by design, and a test has to see it to prove the gateway changes nothing about it.</summary>
    private static Match MatchOf(LoopbackMatchGateway gateway) =>
        (Match)typeof(LoopbackMatchGateway)
            .GetField("_match", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(gateway)!;

    // --- D-61: the exposed snapshot is applied, never handed straight out ---

    [Fact]
    public void AfterARefresh_TheExposedSnapshot_EqualsButIsNotTheOneTheBuilderProduced()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        gateway.Advance(_tick * 40);

        var exposed = gateway.CurrentSnapshot;
        var freshlyBuilt = MatchSnapshotBuilder.Build(MatchOf(gateway), MatchOf(gateway).HumanPlayer);

        Assert.Equal(freshlyBuilt, exposed);
        Assert.NotSame(freshlyBuilt, exposed);

        // The stronger claim, and the one a comment could not make: what is exposed came out of
        // SnapshotApplier, so it cannot be the object the builder returned on that frame either.
        var builtDuringRefresh = MatchSnapshotBuilder.Build(MatchOf(gateway), MatchOf(gateway).HumanPlayer);
        Assert.NotSame(builtDuringRefresh, exposed);
    }

    [Fact]
    public void AnAcceptedCommand_IsVisibleInTheExposedSnapshotImmediately()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        var match = MatchOf(gateway);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        Assert.Empty(gateway.CurrentSnapshot.Armies);

        var result = gateway.Submit(GatewayCommand.SendArmy(human.Id, neutral.Id, SendStrength.Half));

        Assert.True(result.Accepted);
        Assert.NotEmpty(gateway.CurrentSnapshot.Armies);
    }

    // --- once per frame, not once per tick ---

    [Fact]
    public void AdvancingManyTicksInOneCall_ReachesTheSameStateAsAdvancingTickByTick()
    {
        using var oneCall = new LoopbackMatchGateway(MapCatalog.Medium);
        using var tickByTick = new LoopbackMatchGateway(MapCatalog.Medium);

        const int ticks = 137; // deliberately not a multiple of the AI's 40-tick decision interval

        oneCall.Advance(_tick * ticks);
        for (var i = 0; i < ticks; i++)
        {
            tickByTick.Advance(_tick);
        }

        Assert.Equal(ticks, oneCall.CurrentSnapshot.ElapsedTicks);
        Assert.Equal(oneCall.CurrentSnapshot, tickByTick.CurrentSnapshot);
    }

    [Fact]
    public void AFrameShorterThanATick_AdvancesNothing_AndTheRemainderCarries()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);

        gateway.Advance(_tick - 1);
        Assert.Equal(0, gateway.CurrentSnapshot.ElapsedTicks);

        gateway.Advance(1);
        Assert.Equal(1, gateway.CurrentSnapshot.ElapsedTicks);
    }

    // --- commands ---

    [Fact]
    public void ASend_ResolvesToTheSameCountTheOldClientSidePathComputed()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        var match = MatchOf(gateway);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var neutral = match.Bases.First(b => b.Owner is null);

        SetGarrison(human, 37); // an awkward number, so a wrong rounding rule would show
        var expected = SendStrengthCalculator.Compute(37, SendStrength.ThreeQuarters);

        Assert.True(gateway.Submit(GatewayCommand.SendArmy(human.Id, neutral.Id, SendStrength.ThreeQuarters)).Accepted);

        // Measured on the source garrison rather than on what is in flight: a send of this size
        // leaves as several waves (phase 4 FR-3), only the first of which is airborne on the tick it
        // was submitted. What the whole send cost the source is the count the strength resolved to.
        Assert.Equal(37 - expected, gateway.CurrentSnapshot.Bases.Single(b => b.Id == human.Id).GarrisonCount);
    }

    /// <summary>
    /// D-76's no-player-id rule, tested the only way an absence can be: a command names a base and
    /// nothing else, and what comes out is the local human player's army - never the AI's, even
    /// though the source base named is one the AI owns, which the rules then reject.
    /// </summary>
    [Fact]
    public void ACommand_IsAlwaysAttributedToTheLocalHumanPlayer()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        var match = MatchOf(gateway);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        Assert.True(gateway.Submit(GatewayCommand.SendArmy(human.Id, ai.Id, SendStrength.Half)).Accepted);
        Assert.All(gateway.CurrentSnapshot.Armies, a => Assert.Equal(gateway.CurrentSnapshot.LocalPlayerId, a.OwnerPlayerId));

        // Naming an AI-owned base as the source is not a way to act as the AI: it is simply a
        // command the human cannot issue, and it is rejected as one.
        var rejected = gateway.Submit(GatewayCommand.SendArmy(ai.Id, human.Id, SendStrength.Half));
        Assert.False(rejected.Accepted);
        Assert.Equal(SendArmyOutcome.SourceNotOwnedByIssuer.ToString(), rejected.RejectionReason);
    }

    [Fact]
    public void ARejectedCommand_ReportsTheRulesOwnReason_AndChangesNothing()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        var match = MatchOf(gateway);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var before = gateway.CurrentSnapshot;

        SetGarrison(human, 0);
        var result = gateway.Submit(GatewayCommand.Upgrade(human.Id));

        Assert.False(result.Accepted);
        Assert.Equal(UpgradeOutcome.GarrisonBelowCost.ToString(), result.RejectionReason);
        Assert.Same(before, gateway.CurrentSnapshot);
    }

    [Fact]
    public void AnUpgradeAndAConvert_BothReachTheRules()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);
        var match = MatchOf(gateway);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);

        SetGarrison(human, 200);
        Assert.True(gateway.Submit(GatewayCommand.Upgrade(human.Id)).Accepted);
        Assert.Equal(BaseActionKind.Upgrade, SnapshotBase(gateway, human.Id).Construction!.Kind);

        gateway.Advance(_tick * LevelTable.UpgradeBuildDurationTicks(LevelTable.MinLevel));
        SetGarrison(human, 200);

        Assert.True(gateway.Submit(GatewayCommand.Convert(human.Id, BaseType.Tower)).Accepted);
        Assert.Equal(BaseType.Tower, SnapshotBase(gateway, human.Id).Construction!.TargetType);
    }

    [Fact]
    public void Submit_RejectsANullCommand()
    {
        using var gateway = new LoopbackMatchGateway(MapCatalog.Small);

        Assert.Throws<ArgumentNullException>(() => gateway.Submit(null!));
    }

    // --- lifecycle ---

    [Fact]
    public void TwoGateways_ShareNothing_AndDisposingOneLeavesTheOtherPlayable()
    {
        var first = new LoopbackMatchGateway(MapCatalog.Small);
        using var second = new LoopbackMatchGateway(MapCatalog.Small);

        first.Advance(_tick * 200);
        Assert.Equal(200, first.CurrentSnapshot.ElapsedTicks);
        Assert.Equal(0, second.CurrentSnapshot.ElapsedTicks);

        first.Dispose();
        first.Dispose(); // disposing twice is not an error

        second.Advance(_tick * 5);
        Assert.Equal(5, second.CurrentSnapshot.ElapsedTicks);
    }

    // --- the factory ---

    [Fact]
    public void TheFactory_ReportsEveryMapInCatalogueOrder()
    {
        var factory = new LoopbackMatchGatewayFactory();

        Assert.Equal(MapCatalog.AllIds.Select(id => id.ToString()).ToArray(), factory.MapNames.ToArray());
    }

    [Theory]
    [InlineData("Small")]
    [InlineData("medium")] // the case latitude `--map medium` has always had
    [InlineData("BIG")]
    public void TheFactory_CreatesAGatewayOnTheNamedMap(string name)
    {
        var factory = new LoopbackMatchGatewayFactory();

        using var gateway = factory.CreateForMap(name);

        Assert.Equal(name, gateway.CurrentSnapshot.MapId, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFactory_RejectsAnUnknownMapName_WithAMessageNamingTheKnownOnes()
    {
        var factory = new LoopbackMatchGatewayFactory();

        var exception = Assert.Throws<ArgumentException>(() => factory.CreateForMap("enormous"));

        Assert.Contains("enormous", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Small", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFactory_CreatesAFreshMatchEveryTime()
    {
        var factory = new LoopbackMatchGatewayFactory();

        using var first = factory.CreateForMap("Small");
        first.Advance(_tick * 100);

        using var second = factory.CreateForMap("Small");

        Assert.Equal(0, second.CurrentSnapshot.ElapsedTicks);
        Assert.NotSame(first, second);
    }

    private static BaseSnapshot SnapshotBase(LoopbackMatchGateway gateway, int id) =>
        gateway.CurrentSnapshot.Bases.Single(b => b.Id == id);
}
