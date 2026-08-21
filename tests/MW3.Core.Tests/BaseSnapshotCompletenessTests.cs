using System.Text.Json;
using MW3.Transport;

namespace MW3.Core.Tests;

/// <summary>
/// Phase 8 FR-3 added <see cref="BaseSnapshot.RangeUnits"/>, and a field added to a hand-written
/// pipeline is a field three separate places can forget: equality (covered by
/// <see cref="SnapshotEqualityCompletenessTests"/>), the differ, and the JSON contract. This walks
/// <see cref="BaseSnapshot"/>'s primary constructor reflectively and perturbs one parameter at a
/// time, asserting each of the two remaining places notices - so the next field added cannot be
/// forgotten either, and the failure names the field.
/// </summary>
public class BaseSnapshotCompletenessTests
{
    private static void SetGarrison(Base b, int garrison) =>
        typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison });

    /// <summary>A snapshot whose first base is a tower under construction with a populated action list - every nullable field on it is exercised in its non-null state.</summary>
    private static MatchSnapshot BuildSnapshot()
    {
        var match = new Match(MapCatalog.Big);
        var human = match.Bases.Single(b => b.Owner == match.HumanPlayer);

        SetGarrison(human, 200);
        Assert.Equal(ConvertOutcome.Accepted, match.Execute(new ConvertCommand(match.HumanPlayer, human.Id, BaseType.Tower)));
        match.Advance(LevelTable.ConversionBuildDurationTicks);
        SetGarrison(human, 200);
        Assert.Equal(UpgradeOutcome.Accepted, match.Execute(new UpgradeCommand(match.HumanPlayer, human.Id)));

        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);
        Assert.Contains(snapshot.Bases, b => b.RangeUnits is not null);
        return snapshot;
    }

    private static BaseSnapshot WithParameterPerturbed(BaseSnapshot original, int index)
    {
        var constructor = typeof(BaseSnapshot).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            values[i] = typeof(BaseSnapshot).GetProperty(parameters[i].Name!)!.GetValue(original);
        }

        values[index] = Perturb(parameters[index].ParameterType, values[index]);
        return (BaseSnapshot)constructor.Invoke(values);
    }

    private static object? Perturb(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).Cast<object>().First(v => !v.Equals(current));
        }

        if (underlying == typeof(int))
        {
            return (current as int?).GetValueOrDefault() + 7;
        }

        if (underlying == typeof(long))
        {
            return (current as long?).GetValueOrDefault() + 7L;
        }

        if (underlying == typeof(double))
        {
            return (current as double?).GetValueOrDefault() + 7.0;
        }

        if (underlying == typeof(MapPoint))
        {
            var point = current is MapPoint p ? p : default;
            return new MapPoint(point.X + 0.25, point.Y + 0.25);
        }

        if (underlying == typeof(PendingConstructionSnapshot))
        {
            return current is null ? new PendingConstructionSnapshot(BaseActionKind.Upgrade, 42, 3, null) : null;
        }

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying))
        {
            var list = (System.Collections.IList)current!;
            var shortened = new List<BaseActionSnapshot>();
            for (var i = 0; i < list.Count - 1; i++)
            {
                shortened.Add((BaseActionSnapshot)list[i]!);
            }

            return shortened;
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"This test has no way to perturb a {type}. Add one rather than skipping the field."));
    }

    public static IEnumerable<object[]> EveryConstructorParameter() =>
        typeof(BaseSnapshot).GetConstructors().Single().GetParameters()
            .Select((p, i) => new object[] { i, p.Name! });

    [Theory]
    [MemberData(nameof(EveryConstructorParameter))]
    public void EveryBaseSnapshotField_IsVisitedByTheDiffer(int index, string parameterName)
    {
        var before = BuildSnapshot();
        var target = before.Bases.Single(b => b.RangeUnits is not null && b.Construction is not null);

        // Id is the identity the differ pairs bases by, so a "changed" id is a different base, not a
        // change - it is the one parameter this claim does not apply to, and saying so explicitly is
        // better than quietly excluding it from the data.
        if (parameterName == nameof(BaseSnapshot.Id))
        {
            return;
        }

        var mutated = WithParameterPerturbed(target, index);
        var after = new MatchSnapshot(
            before.ProtocolVersion,
            before.MapId,
            before.ElapsedTicks,
            before.Outcome,
            before.LocalPlayerId,
            before.Obstacles,
            before.Players,
            before.Bases.Select(b => b.Id == target.Id ? mutated : b).ToList(),
            before.Armies);

        var batch = SnapshotDiffer.Diff(before, after);

        Assert.True(
            batch.Events.Any(e => e.BaseId == target.Id),
            $"BaseSnapshot.{parameterName} changed and the differ produced no event for it - a client would never learn about it.");
    }

    [Theory]
    [MemberData(nameof(EveryConstructorParameter))]
    public void EveryBaseSnapshotField_SurvivesTheJsonContract(int index, string parameterName)
    {
        var snapshot = BuildSnapshot();
        var target = snapshot.Bases.Single(b => b.RangeUnits is not null && b.Construction is not null);
        var mutated = WithParameterPerturbed(target, index);

        var json = JsonSerializer.Serialize(mutated, WireJsonContext.Default.BaseSnapshot);
        var restored = JsonSerializer.Deserialize(json, WireJsonContext.Default.BaseSnapshot);

        Assert.True(
            mutated.Equals(restored),
            $"BaseSnapshot.{parameterName} did not survive a JSON round trip - the serializer is not carrying it.");
    }

    [Fact]
    public void OnlyATower_CarriesARange_AndItIsTheOneItsLevelDefines()
    {
        var match = new Match(MapCatalog.Big);
        var snapshot = MatchSnapshotBuilder.Build(match, match.HumanPlayer);

        foreach (var b in snapshot.Bases)
        {
            if (b.Type == BaseType.Tower)
            {
                Assert.Equal(LevelTable.Tower.RangeUnits(b.Level), b.RangeUnits);
            }
            else
            {
                Assert.Null(b.RangeUnits);
            }
        }

        Assert.Contains(snapshot.Bases, b => b.Type == BaseType.Tower);
    }

    [Fact]
    public void TheProtocolVersion_WasBumpedForTheAddedField() =>
        Assert.Equal(3, MatchSnapshot.CurrentProtocolVersion);
}
