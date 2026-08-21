using System.Text.Json;
using MW3.Transport;

namespace MW3.Core.Tests;

/// <summary>An <see cref="EventBatch"/> round-trips through the same source-generated context <c>MatchSnapshot</c> does (FR-2 acceptance).</summary>
public class EventBatchJsonTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    private static EventBatch BuildRichBatch()
    {
        var match = new Match(MapCatalog.Medium);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);
        var ai = match.Bases.Single(b => b.Owner == match.AiPlayer);
        var a = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        SetGarrison(human, 60);
        Assert.Equal(SendArmyOutcome.Accepted, match.Execute(new SendArmyCommand(match.HumanPlayer, human.Id, ai.Id, 30)));
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));
        match.Advance(200);

        var b = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        var batch = SnapshotDiffer.Diff(a, b);

        Assert.NotEmpty(batch.Events);
        return batch;
    }

    [Fact]
    public void ABatch_RoundTripsThroughJson_Unchanged()
    {
        var original = BuildRichBatch();

        var json = JsonSerializer.Serialize(original, WireJsonContext.Default.EventBatch);
        var restored = JsonSerializer.Deserialize(json, WireJsonContext.Default.EventBatch);

        Assert.Equal(original, restored);
    }
}
