using System.Text.Json;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-1: the snapshot is the JSON contract (D-64), so it has to survive a round trip through
/// <see cref="JsonSerializer"/> unchanged - every field, including the nested paths and actions that
/// a shallow comparison would let through. Nothing transmits one until FR-4; this is the test that
/// says it could.
/// </summary>
public class MatchSnapshotJsonTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    /// <summary>
    /// A match carrying as much shape as one can: an obstacle list, a detoured multi-waypoint path,
    /// several waves of one send in flight, a construction in progress, and a base the local player
    /// owns (so its action list is populated). A round trip that only ever saw an empty match would
    /// prove very little.
    /// </summary>
    private static MatchSnapshot BuildRichSnapshot()
    {
        var match = new Match(MapCatalog.Medium);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);

        SetGarrison(human, 40);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 30)));
        match.Advance(20);

        SetGarrison(human, 60);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));

        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        Assert.NotEmpty(snapshot.Obstacles);
        Assert.True(snapshot.Armies.Count > 1, "The fixture is meant to have several waves in flight.");
        Assert.True(snapshot.Armies[0].PathWaypoints.Count > 2, "The fixture's send is meant to be detoured.");
        Assert.Contains(snapshot.Bases, b => b.Construction is not null);
        Assert.Contains(snapshot.Bases, b => b.AvailableActions.Count > 0);

        return snapshot;
    }

    private static MatchSnapshot RoundTrip(MatchSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, MatchSnapshotJsonContext.Default.MatchSnapshot);
        return JsonSerializer.Deserialize(json, MatchSnapshotJsonContext.Default.MatchSnapshot)!;
    }

    [Fact]
    public void ASnapshot_RoundTripsThroughJson_Unchanged()
    {
        var original = BuildRichSnapshot();

        var restored = RoundTrip(original);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void TheRoundTrip_PreservesEveryNestedPathAndActionFieldByField()
    {
        // Equality is structural on purpose (SnapshotEquality), so the assertion above already sees
        // through the lists. This one spells the nested shapes out, because the failure it guards
        // against - a waypoint list that survives as the right length but the wrong points - is
        // exactly the kind a lazily-written Equals would hide.
        var original = BuildRichSnapshot();

        var restored = RoundTrip(original);

        Assert.Equal(original.Obstacles, restored.Obstacles);
        Assert.Equal(original.Players, restored.Players);

        for (var i = 0; i < original.Bases.Count; i++)
        {
            Assert.Equal(original.Bases[i].Construction, restored.Bases[i].Construction);
            Assert.Equal(original.Bases[i].AvailableActions, restored.Bases[i].AvailableActions);
            Assert.Equal(original.Bases[i].Position, restored.Bases[i].Position);
        }

        for (var i = 0; i < original.Armies.Count; i++)
        {
            Assert.Equal(original.Armies[i].PathWaypoints, restored.Armies[i].PathWaypoints);
            Assert.Equal(original.Armies[i].PathLength, restored.Armies[i].PathLength);
        }
    }

    [Fact]
    public void ARestoredSnapshot_StillResolvesArmyPositionsIdentically()
    {
        // The point of carrying launch data instead of a position: a client that has only been
        // handed JSON can still place every army at any tick, through the same function the rules
        // use (D-68).
        var original = BuildRichSnapshot();
        var restored = RoundTrip(original);

        for (var i = 0; i < original.Armies.Count; i++)
        {
            var before = original.Armies[i];
            var after = restored.Armies[i];

            for (var tick = before.LaunchTick; tick <= before.ArrivalTick; tick += 3)
            {
                Assert.Equal(
                    ArmyPathMath.PositionAt(before.ToPath(), before.LaunchTick, before.ArrivalTick, tick),
                    ArmyPathMath.PositionAt(after.ToPath(), after.LaunchTick, after.ArrivalTick, tick));
            }
        }
    }

    [Fact]
    public void TheSerializedForm_CarriesTheProtocolVersion()
    {
        var json = JsonSerializer.Serialize(BuildRichSnapshot(), MatchSnapshotJsonContext.Default.MatchSnapshot);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            MatchSnapshot.CurrentProtocolVersion,
            document.RootElement.GetProperty(nameof(MatchSnapshot.ProtocolVersion)).GetInt32());
    }

    [Fact]
    public void TheSerializedForm_CarriesEachPlayerExactlyOnce()
    {
        // The local player is named by id and looked up through a method, never exposed as a
        // property - a gettable property would be serialized, putting a second copy of one player's
        // record on the wire that the deserializer then drops. Two encodings of one fact is how a
        // payload starts being able to contradict itself.
        var json = JsonSerializer.Serialize(BuildRichSnapshot(), MatchSnapshotJsonContext.Default.MatchSnapshot);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty(nameof(MatchSnapshot.Players)).GetArrayLength());
        Assert.False(
            document.RootElement.TryGetProperty("LocalPlayer", out _),
            "The snapshot serialized a second copy of the local player.");
    }

    [Fact]
    public void TheSerializedForm_CarriesNoArmyPositionOrProgress()
    {
        // Not a style preference: a position on the wire would be a second answer to "where is this
        // army", and the whole point of D-68 is that there is only one (see ArmyPathMath).
        var json = JsonSerializer.Serialize(BuildRichSnapshot(), MatchSnapshotJsonContext.Default.MatchSnapshot);

        using var document = JsonDocument.Parse(json);
        foreach (var army in document.RootElement.GetProperty(nameof(MatchSnapshot.Armies)).EnumerateArray())
        {
            foreach (var property in army.EnumerateObject())
            {
                Assert.DoesNotContain("Position", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Progress", property.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
