namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-3's central claim, and the only one that can be made end to end: <b>the gateway
/// changes nothing about the simulation</b>. Each test drives a whole match on one shipped map to a
/// decided outcome through <see cref="LoopbackMatchGateway"/>, submitting sends, an upgrade and a
/// convert, and drives a second, bare <see cref="Match"/> with the identical command sequence and
/// tick advances. The two must agree exactly at the end - so the entire diff/apply pipeline the
/// gateway runs on every frame (D-61) is proven to be a faithful identity over a real match, not
/// only over the pairs FR-2's own tests picked.
/// </summary>
public class LoopbackMatchGatewayIntegrationTests
{
    private const long _tick = Match.TickDurationMilliseconds;

    /// <summary>Enough ticks for the AI to grind out a decision on any of the three maps; the drive loop stops the moment one is reached.</summary>
    private const int _maxTicks = 40_000;

    /// <summary>How many ticks each step of the drive loop advances - several ticks at a time, which is what exercises FR-2's non-adjacent diff on every step.</summary>
    private const int _ticksPerStep = 7;

    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static Match MatchOf(LoopbackMatchGateway gateway) =>
        (Match)typeof(LoopbackMatchGateway)
            .GetField("_match", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(gateway)!;

    [Theory]
    [InlineData("Small")]
    [InlineData("Medium")]
    [InlineData("Big")]
    public void AWholeMatchDrivenThroughTheGateway_EndsIdenticalToOneDrivenDirectly(string mapName)
    {
        var factory = new LoopbackMatchGatewayFactory();
        using var gateway = (LoopbackMatchGateway)factory.CreateForMap(mapName);

        // The parallel match: same map, same fresh AI brain, driven with the same commands at the
        // same ticks. Nothing about it goes through the protocol.
        var parallel = new Match(MapCatalog.Get(Enum.Parse<MapId>(mapName, ignoreCase: true)));
        var parallelRunner = new MatchRunner(parallel, new AiBrain(parallel.AiPlayer));

        var gatewayMatch = MatchOf(gateway);
        var humanBaseId = gatewayMatch.Bases.Single(b => b.Owner == gatewayMatch.HumanPlayer).Id;
        var neutralBaseId = gatewayMatch.Bases.First(b => b.Owner is null).Id;
        var aiBaseId = gatewayMatch.Bases.Single(b => b.Owner == gatewayMatch.AiPlayer).Id;

        var elapsed = 0;
        var upgraded = false;
        var converted = false;
        var sends = 0;

        while (elapsed < _maxTicks && gateway.CurrentSnapshot.Outcome == MatchOutcome.InProgress)
        {
            // An upgrade, then a convert, then a stream of sends - all three command kinds, each
            // applied to both matches at the same tick and with the same effect on the garrison the
            // send strength is resolved against.
            if (!upgraded && elapsed >= 100)
            {
                Force(gatewayMatch, parallel, humanBaseId, 200);
                Assert.True(gateway.Submit(GatewayCommand.Upgrade(humanBaseId)).Accepted);
                Assert.Equal(UpgradeOutcome.Accepted, parallelRunner.Execute(new UpgradeCommand(parallel.HumanPlayer, humanBaseId)));
                upgraded = true;
            }
            else if (upgraded && !converted && elapsed >= 400)
            {
                Force(gatewayMatch, parallel, neutralBaseId, 200);
                var accepted = gateway.Submit(GatewayCommand.Convert(neutralBaseId, BaseType.Tower)).Accepted;
                var parallelOutcome = parallelRunner.Execute(new ConvertCommand(parallel.HumanPlayer, neutralBaseId, BaseType.Tower));
                Assert.Equal(accepted, parallelOutcome == ConvertOutcome.Accepted);
                converted = true;
            }
            else if (elapsed >= 600 && elapsed % 100 < _ticksPerStep)
            {
                // Deliberately overwhelming, and aimed at whatever the human does not own yet: the
                // point of this test is that two runs agree, so the match has to reach a decision in
                // bounded time rather than grind out whatever stalemate the AI would settle into.
                // Source and target are chosen from the parallel match by lowest id, which is a pure
                // function of state both sides share - the moment they stopped sharing it, this test
                // would fail for the reason it exists.
                var source = LowestId(parallel, owned: true);
                var target = LowestId(parallel, owned: false);
                if (source is int sourceId && target is int targetId)
                {
                    Force(gatewayMatch, parallel, sourceId, 200);
                    var gatewayAccepted = gateway.Submit(GatewayCommand.SendArmy(sourceId, targetId, SendStrength.Full)).Accepted;

                    // The parallel side resolves the count exactly as the gateway does, out of the
                    // same calculator and the same garrison - which is the point: the
                    // strength-to-count rule is one rule with one implementation, and the gateway is
                    // simply where it now runs.
                    var garrison = parallel.Bases.Single(b => b.Id == sourceId).GarrisonCount;
                    var count = SendStrengthCalculator.Compute(garrison, SendStrength.Full);
                    var parallelOutcome = parallelRunner.Execute(new SendArmyCommand(parallel.HumanPlayer, sourceId, targetId, count));
                    Assert.Equal(gatewayAccepted, parallelOutcome == SendArmyOutcome.Accepted);
                    sends++;
                }
            }

            gateway.Advance(_tick * _ticksPerStep);
            parallelRunner.Advance(_ticksPerStep);
            elapsed += _ticksPerStep;
        }

        Assert.True(upgraded, "the fixture is meant to submit an upgrade");
        Assert.True(converted, "the fixture is meant to submit a convert");
        Assert.True(sends > 0, "the fixture is meant to submit sends");
        Assert.NotEqual(MatchOutcome.InProgress, gateway.CurrentSnapshot.Outcome);

        var expected = MatchSnapshotBuilder.Build(parallel, parallel.HumanPlayer);
        Assert.Equal(expected, gateway.CurrentSnapshot);
    }

    /// <summary>The lowest-id base the human does (or does not) own, or null if there is none.</summary>
    private static int? LowestId(Match match, bool owned)
    {
        foreach (var b in match.Bases.OrderBy(b => b.Id))
        {
            if ((b.Owner == match.HumanPlayer) == owned)
            {
                return b.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets one base's garrison identically in both matches, so a command that depends on it (an
    /// upgrade's affordability, a send's resolved count) is decided on the same number on both sides.
    /// Reaching in like this is what makes the drive deterministic rather than a race against
    /// production.
    /// </summary>
    private static void Force(Match gatewayMatch, Match parallel, int baseId, int garrison)
    {
        var a = gatewayMatch.Bases.Single(b => b.Id == baseId);
        var b2 = parallel.Bases.Single(b => b.Id == baseId);

        // Only when both are still the human's: once a base has changed hands the two matches have
        // to be left alone to agree on their own, which is the stronger claim anyway.
        if (a.Owner == gatewayMatch.HumanPlayer && b2.Owner == parallel.HumanPlayer)
        {
            SetGarrison(a, garrison);
            SetGarrison(b2, garrison);
        }
    }
}
